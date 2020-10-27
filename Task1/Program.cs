using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ImageClassification;

namespace Task1
{
    class Program
    {
        static void Prediction(object sender, EventArgs e)
        {
            StringPath item;
            if ((ConcurrentQueue<StringPath>)sender != null)
            {
                ((ConcurrentQueue<StringPath>)sender).TryDequeue(out item);
                Console.WriteLine($"file: {item.sPath} result: {item.sLabel}");
            }
        }

        static void Main(string[] args)
        {
            string modelpath, impath;

            Console.WriteLine("Enter full path to the model directory:");
            modelpath = Console.ReadLine();

            Console.WriteLine("Enter full path to the image directory:");
            impath = Console.ReadLine();

            ImageClass class_im = new ImageClass(modelpath);
            class_im.Notify += Prediction;

            Thread cancelTread = new Thread(() =>
            {
                while (true)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.C)
                    {
                        class_im.Stop();
                        break;
                    }
                    if (ImageClass.end_source == Directory.GetFiles(impath, "*.jpg").Length)
                        break;
                }
            }
           );
            cancelTread.Start();
            class_im.ParallelProcess(impath);

            class_im.ParallelProcess(impath);
        }
    }
}