using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ImageClassification;

namespace Task1
{
    class Program
    {
        static void Prediction(StringPath sender, EventArgs e)
        {
            if (sender != null)
            {
                Console.WriteLine($"file: {sender.sPath} result: {sender.sLabel}");
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
        }
    }
}