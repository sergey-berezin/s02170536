using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageClassification;

namespace Task2
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ImageClass pred = null;
        private ObservableCollection<ClassName> className;
        private ObservableCollection<Image> images;
        private ObservableCollection<StringPath> results;
        private ConcurrentQueue<string> imageNames;
        private ObservableCollection<Image> selectedImages;
        private ICollectionView listUpdate;
        private Thread[] threads;

        public void Output(StringPath result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                results.Add(result);
                var q = from i in className
                        where i.Class == result.sLabel
                        select i;
                if (q.Count() == 0)
                {
                    className.Add(new ClassName(result.sLabel));
                }
                else
                {
                    q.First().Num++;
                    listUpdate.Refresh();
                }

                for (int i = 0; i < images.Count; ++i)
                {
                    if (images[i].Path == result.sPath)
                    {
                        images[i].Class = result.sLabel;
                        break;
                    }
                }
            }));
        }

        public MainWindow()
        {
            InitializeComponent();
            threads = new Thread[Environment.ProcessorCount];
            for (int i = 0; i < Environment.ProcessorCount; ++i)
            {
                threads[i] = null;
            }
            setBindings();
        }

        private void setBindings()
        {
            images = new ObservableCollection<Image>();
            results = new ObservableCollection<StringPath>();
            className = new ObservableCollection<ClassName>();
            selectedImages = new ObservableCollection<Image>();

            Binding img2list = new Binding();
            img2list.Source = images;
            listboxImages.SetBinding(ItemsControl.ItemsSourceProperty, img2list);

            Binding numresults = new Binding();
            numresults.Source = results;
            numresults.Path = new PropertyPath("Count");
            numResult.SetBinding(TextBlock.TextProperty, numresults);

            Binding class_num = new Binding();
            class_num.Source = className;
            listboxClasses.SetBinding(ItemsControl.ItemsSourceProperty, class_num);
            listUpdate = CollectionViewSource.GetDefaultView(listboxClasses.ItemsSource);

            Binding filtered = new Binding();
            filtered.Source = selectedImages;
            listboxSelectedImages.SetBinding(ItemsControl.ItemsSourceProperty, filtered);
        }

        private void thread_method()
        {
            string path;
            while (imageNames.TryDequeue(out path))
            {
                string close_path = path;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    images.Add(new Image(close_path));
                }));
            }
        }

        private void Open(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.SelectedPath = "..\\..\\..\\..\\images";
                    System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                    if (result != System.Windows.Forms.DialogResult.OK) return;
                    pred?.Stop();
                    foreach (Thread t in threads)
                    {
                        t?.Join();
                    }
                    imageNames = new ConcurrentQueue<string>(Directory.GetFiles(dialog.SelectedPath, "*.jpeg"));
                    results.Clear();
                    images.Clear();
                    className.Clear();

                    for (int i = 0; i < Environment.ProcessorCount; ++i)
                    {
                        threads[i] = new Thread(thread_method);
                        threads[i].Start();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Stop(object sender, RoutedEventArgs e)
        {
            if (pred != null)
            {
                pred.Stop();
            }
        }

        private void listboxClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedImages.Clear();
            ClassName selected = listboxClasses.SelectedItem as ClassName;
            if (selected == null) return;
            foreach (var t in images)
            {
                if (t.Class == selected.Class)
                {
                    selectedImages.Add(t);
                }
            }
        }
    }

    public class ClassName : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Class { get; set; }
        private int num;
        public int Num
        {
            get
            {
                return num;
            }
            set
            {
                num = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Num"));
            }
        }

        public ClassName(string classLabel)
        {
            Num = 1;
            Class = classLabel;
        }

        public override string ToString()
        {
            return Class + ": " + Num;
        }
    }

    public class Image : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Path { get; set; }
        private string classLabel;
        public string Class
        {
            get
            {
                return classLabel;
            }
            set
            {
                classLabel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Class"));
            }
        }
        public BitmapImage Bitmap { get; set; }

        public Image(string path)
        {
            Path = path;
            if (path == null)
            {
                Console.WriteLine("Error here");
            }
            Bitmap = new BitmapImage(new Uri(path));
            Class = "";
        }
    }
}