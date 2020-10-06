using System;
using System.Collections.Concurrent;
using ImageClassification;

namespace Task1
{
    class Program
    {

        public static void Output(bool msg)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.C)
                msg = true;
            else
                msg = false;
        }

        static void Prediction(object sender, EventArgs e)
        {
            string item;
            ((ConcurrentQueue<string>)sender).TryDequeue(out item);
            Console.WriteLine(item.ToString());
        }

        static void Main(string[] args)
        {
            string modelpath, impath;
            
            Console.WriteLine("Enter full path to the model directory:");
            modelpath = Console.ReadLine();
            
            Console.WriteLine("Enter full path to the image directory:");
            impath = Console.ReadLine();
            
            ImageClass class_im = new ImageClass(modelpath, Output);
            class_im.Notify += Prediction;
            class_im.ParallelProcess(impath);
        }
    }
}