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

namespace ImageClassification
{

    public class StringPath
    {
        public string sPath;
        public string sLabel;
        public StringPath(string path, string label)
        {
            this.sPath = path;
            this.sLabel = label;
        }
    }

    public class ImageClass
    {
        public static InferenceSession Session;
        public ConcurrentQueue<StringPath> ResultPull;

        public ImageClass(string modelPath)
        {
            Session = new InferenceSession(modelPath);

        }

        public void Stop()
        {
            source.Cancel();
        }
        public void Process(string imagePath, int tmp)
        {
            using (var image = Image.Load<Rgb24>((string)imagePath ?? "image.jpg"))
            {
                const int TargetWidth = 224;
                const int TargetHeight = 224;

                // resize to 224 x 224
                image.Mutate(x =>
                {
                    x.Resize(new ResizeOptions
                    {
                        Size = new Size(TargetWidth, TargetHeight),
                        Mode = ResizeMode.Crop
                    });
                });

                // pixels to tenzors + normalize
                var input = new DenseTensor<float>(new[] { 1, 3, TargetHeight, TargetWidth });
                var mean = new[] { 0.485f, 0.456f, 0.406f };
                var stddev = new[] { 0.229f, 0.224f, 0.225f };
                for (int y = 0; y < TargetHeight; y++)
                {
                    Span<Rgb24> pixelSpan = image.GetPixelRowSpan(y);
                    for (int x = 0; x < TargetWidth; x++)
                    {
                        input[0, 0, y, x] = ((pixelSpan[x].R / 255f) - mean[0]) / stddev[0];
                        input[0, 1, y, x] = ((pixelSpan[x].G / 255f) - mean[1]) / stddev[1];
                        input[0, 2, y, x] = ((pixelSpan[x].B / 255f) - mean[2]) / stddev[2];
                    }
                }


                // input for onnx
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(Session.InputMetadata.Keys.First(),input)
                };

                // prediction
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = Session.Run(inputs))
                {

                    var output = results.First().AsEnumerable<float>().ToArray();
                    var sum = output.Sum(x => (float)Math.Exp(x));
                    var softmax = output.Select(x => (float)Math.Exp(x) / sum);

                    int index = softmax.ToList().IndexOf(softmax.Max());

                    TryEqueueEvent(new StringPath((string)imagePath, LabelClass.Labels[index]), ResultPull);
                }
            }
        }

        public void TryEqueueEvent(StringPath newList, ConcurrentQueue<StringPath> res)
        {
            res.Enqueue(newList);
            Notify?.Invoke(res, new EventArgs());
        }

        public delegate void AccountHandler(object sender, EventArgs e);
        public event AccountHandler Notify;
        public static CancellationTokenSource source = new CancellationTokenSource();
        public static int end_source;

        public void ParallelProcess(string dirPath)
        {
            string[] files = Directory.GetFiles(dirPath, "*.jpg");
            ResultPull = new ConcurrentQueue<StringPath>();
            var events = new AutoResetEvent[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                events[i] = new AutoResetEvent(false);
                ThreadPool.QueueUserWorkItem(tmp =>
                {
                    int count = (int)tmp;
                    if (!source.Token.IsCancellationRequested)
                        Process(files[count], count);
                    Interlocked.Increment(ref end_source);
                    events[count].Set();

                }, i);
            }

            for (int i = 0; i < files.Length; i++)
            {
                events[i].WaitOne();
            }

        }

    }
}