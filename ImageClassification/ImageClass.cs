using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Linq;
using SixLabors.ImageSharp.Processing;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.OnnxRuntime;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML.Transforms.Text;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Advanced;
using Microsoft.EntityFrameworkCore.SqlServer;

namespace ImageClassification
{
    class ResultContext : DbContext
    {
        public DbSet<Result> Results { get; set; }
        public DbSet<ImageData> Images { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer(@"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=C:\Users\Администратор\source\repos\ImageClassification\Task3\Library.mdf;Integrated Security=True");
    }

    public class ImageData
    {
        [Key]
        public int ImageDataId { get; set; }
        public byte[] Data { get; set; }
    }

    public class Result
    {
        [Key]
        public int ItemId { get; set; }
        public string Label { get; set; }
        public string Path { get; set; }
        public float Confidence { get; set; }
        public virtual ImageData Blob { get; set; }

        /*public Result(string l, string p = "C:\\Users\\Администратор\\source\\repos\\ImageClassification\\images")
        {
            sLabel = l;
            sPath = p;
        }*/

        public override string ToString()
        {
            if (Path == null)
            {
                return Label;
            }
            return $"{Label} with confidence {Confidence} for image {Path}";
        }
    }

    public class ImageClass
    {
        public delegate void Output(Result sp);
        Output write;

        private string imagePath;
        private string modelPath;
        private int processorCount;
        private int counter;
        private int counterMax;
        private InferenceSession Session;
        private ConcurrentQueue<string> FileNames;
        private AutoResetEvent OutMutex;
        private ManualResetEvent Cancel;

        public ImageClass(Output write,
                          string imagePath = "C:\\Users\\Администратор\\source\repos\\ImageClassification\\images",
                          string modelPath = "C:\\Users\\Администратор\\source\\repos\\ImageClassification\\ImageClassification\\resnet34-v2-7.onnx"
                          )
        {
            this.write += write;
            this.modelPath = modelPath;
            this.imagePath = imagePath;
            processorCount = Environment.ProcessorCount;
            Session = new InferenceSession(modelPath);
            OutMutex = new AutoResetEvent(true);
            Cancel = new ManualResetEvent(false);
        }

        public void ProcessDirectory()
        {
            counter = 0;
            FileNames = new ConcurrentQueue<string>(Directory.GetFiles(imagePath, "*.jpg"));
            counterMax = FileNames.Count;
            OutMutex = new AutoResetEvent(true);
            Cancel = new ManualResetEvent(false);

            Thread[] threads = new Thread[processorCount];
            for (int i = 0; i < processorCount; ++i)
            {
                threads[i] = new Thread(ThreadMethod);
                threads[i].Start();
            }
        }

        private void ThreadMethod()
        {
            string path;
            while (FileNames.TryDequeue(out path))
            {
                if (Cancel.WaitOne(0))
                {
                    write(new Result { Label = "Interrupted", Blob = null, Confidence = 0.0f, Path = "" });
                    return;
                }

                using (var image = Image.Load<Rgb24>(path))
                {
                    var _IMemoryGroup = image.GetPixelMemoryGroup();
                    var _MemoryGroup = _IMemoryGroup.ToArray()[0];
                    byte[] blob = MemoryMarshal.AsBytes(_MemoryGroup.Span).ToArray();
                    Result info;

                    if (CheckInDB(blob, path, out info))
                    {
                        Console.WriteLine("Found identical: " + info);
                        write(info);
                        return;
                    }

                    Console.WriteLine("No identical found, processing");
                    ImageProcess(image, path, blob);
                }
            }
        }

        private void ImageProcess(Image<Rgb24> image, string path, byte[] blob)
        {

            const int targetHeight = 224;
            const int targetWidth = 224;

            image.Mutate(x =>
            {
                x.Resize(new ResizeOptions
                {
                    Size = new Size(targetWidth, targetHeight),
                    Mode = ResizeMode.Crop
                });
            });

            var input = new DenseTensor<float>(new[] { 1, 3, targetHeight, targetWidth });
            var mean = new[] { 0.485f, 0.456f, 0.406f };
            var stddev = new[] { 0.229f, 0.224f, 0.225f };
            for (int y = 0; y < targetHeight; y++)
            {
                Span<Rgb24> pixelSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < targetWidth; x++)
                {
                    input[0, 0, y, x] = ((pixelSpan[x].R / 255f) - mean[0]) / stddev[0];
                    input[0, 1, y, x] = ((pixelSpan[x].G / 255f) - mean[1]) / stddev[1];
                    input[0, 2, y, x] = ((pixelSpan[x].B / 255f) - mean[2]) / stddev[2];
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("data", input)
            };

            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = Session.Run(inputs))
            {

                var output = results.First().AsEnumerable<float>().ToArray();
                var sum = output.Sum(x => (float)Math.Exp(x));
                var softmax = output.Select(x => (float)Math.Exp(x) / sum);

                foreach (var p in softmax
                    .Select((x, i) => new { Label = LabelClass.Labels[i], Confidence = x })
                    .OrderByDescending(x => x.Confidence)
                    .Take(1))
                {
                    OutMutex.WaitOne(0);
                    var result = new Result { Label = p.Label, Confidence = p.Confidence, Path = path, Blob = new ImageData { Data = blob } };
                    write(result);
                    Postprocess(result);
                    OutMutex.Set();
                }
            }
        }

        private void Postprocess(Result result)
        {
            counter += 1;
            using (var db = new ResultContext())
            {
                Console.WriteLine("Added new entity");
                db.Add(result);
                db.SaveChanges();
            }
        }

        public void Stop() => Cancel.Set();

        private bool CheckInDB(byte[] blob, string path, out Result info)
        {
            info = null;
            using (var db = new ResultContext())
            {
                var query = db.Results.Where(a => a.Path == path);
                if (query.Count() == 0)
                {
                    return false;
                }
                foreach (var result in query)
                {
                    info = new Result { Label = result.Label, Confidence = result.Confidence, Path = result.Path, Blob = new ImageData { Data = blob } };
                    if (result.Blob.Data.Length != blob.Length)
                    {
                        return false;
                    }
                    for (int i = 0; i < blob.Length; ++i)
                    {
                        if (blob[i] != result.Blob.Data[i])
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        public string DatabaseStats()
        {
            string ret = "";
            using (var db = new ResultContext())
            {
                foreach (var classLabel in LabelClass.Labels)
                {
                    int count = db.Results.Count(a => a.Label == classLabel);
                    if (count > 0)
                    {
                        ret += $"{classLabel}: {count}\r\n";
                    }
                }

                return ret;
            }
        }

        public void ClearDatabase()
        {
            this.Stop();
            Console.WriteLine("Clearing database");
            using (var db = new ResultContext())
            {
                foreach (var result in db.Results)
                {
                    db.Remove(result);
                }

                db.SaveChanges();
            }
        }

        public string ImagePath
        {
            get
            {
                return imagePath;
            }
            set
            {
                imagePath = value;
            }
        }

        public override string ToString()
        {
            return $"Image: {imagePath}; Model: {modelPath}; Processors: {processorCount}.";
        }

    }
}