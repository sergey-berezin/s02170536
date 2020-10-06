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

    struct StringPath
    {
        public string sPath;
        public string sLabel;
        public ConcurrentQueue<string> res;
    }

        public class ImageClass
    {

        public delegate void Output(bool msg);
        Output Check;

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
                    NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(),input)
                };

                // prediction
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs))
                {

                    var output = results.First().AsEnumerable<float>().ToArray();
                    var sum = output.Sum(x => (float)Math.Exp(x));
                    var softmax = output.Select(x => (float)Math.Exp(x) / sum);

                    int index = softmax.ToList().IndexOf(softmax.Max());

                    StringPath resPath;
                    resPath.sPath = imagePath;
                    resPath.sLabel = LabelClass.Labels[index];
                    resPath.res = resultList;

                    TryEqueueEvent(resPath.sPath + resPath.sLabel, resPath.res);
                }
        }

        }

        public ImageClass(string modelPath, Output Check)
        {
            session = new InferenceSession(modelPath);
        }

        public static InferenceSession session;
        public ConcurrentQueue<string> resultList;

        public void TryEqueueEvent(string newList, ConcurrentQueue<string> res)
        {
            res.Enqueue(newList);
            Notify?.Invoke(res, new EventArgs());
        }

        public delegate void AccountHandler(object sender, EventArgs e);
        public event AccountHandler Notify;
        public static CancellationTokenSource source;
        public static CancellationTokenSource end_source;
        
        public void ParallelProcess(string dirPath)
        {
            string[] files = Directory.GetFiles(dirPath, "*.jpg");

            resultList = new ConcurrentQueue<string>();

            source = new CancellationTokenSource();
            end_source = new CancellationTokenSource();

            bool msg_check = false;

            Thread cancelTread = new Thread(() =>
            {
                while (true)
                {
                    if (ImageClass.end_source.Token.IsCancellationRequested)
                    {
                        source.Cancel();
                        break;
                    }
                    Check(msg_check);
                    if (msg_check)
                    {
                        source.Cancel();
                        break;
                    }
                }
            }
            );
            cancelTread.Start();
            
            var events = new AutoResetEvent[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                events[i] = new AutoResetEvent(false);

                ThreadPool.QueueUserWorkItem(tmp =>
                {
                    int count = (int)tmp;

                    if (!source.Token.IsCancellationRequested)
                        Process(files[count], count);
                    events[count].Set();
                    if (count == files.Length - 1)
                    {
                        end_source.Cancel();
                    }
                }, i);
            }
                       
            for (int i = 0; i < files.Length; i++)
            {
                events[i].WaitOne();
            }

            cancelTread.Join();
        }

    }
}
