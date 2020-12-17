using System;
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

namespace Task3
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ImageClass pred = null;
        private ObservableCollection<ClassName> className;
        private ObservableCollection<Image> images;
        private ObservableCollection<Result> results;
        private ConcurrentQueue<string> imageNames;
        private ObservableCollection<Image> selectedImages;
        private ICollectionView listUpdate;
        private Thread[] threads;

        public void Output(Result result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                results.Add(result);
                var q = from i in className
                        where i.Label == result.Label
                        select i;
                if (q.Count() == 0)
                {
                    className.Add(new ClassName(result.Label));
                }
                else
                {
                    q.First().Num++;
                    listUpdate.Refresh();
                }

                for (int i = 0; i < images.Count; ++i)
                {
                    if (images[i].Path == result.Path)
                    {
                        images[i].Label = result.Label;
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
            results = new ObservableCollection<Result>();
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

        private void ThreadMethod()
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
                    dialog.SelectedPath = "C:\\Users\\Администратор\\source\\repos\\ImageClassification\\images";
                    System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                    if (result != System.Windows.Forms.DialogResult.OK) return;
                    pred?.Stop();
                    foreach (Thread t in threads)
                    {
                        t?.Join();
                    }
                    imageNames = new ConcurrentQueue<string>(Directory.GetFiles(dialog.SelectedPath, "*.jpg"));
                    results.Clear();
                    images.Clear();
                    className.Clear();

                    for (int i = 0; i < Environment.ProcessorCount; ++i)
                    {
                        threads[i] = new Thread(ThreadMethod);
                        threads[i].Start();
                    }

                    if (pred == null)
                    {
                        pred = new ImageClass(Output, dialog.SelectedPath);
                    }
                    else
                    {
                        pred.ImagePath = dialog.SelectedPath;
                    }
                    pred.ProcessDirectory();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Stop(object sender, RoutedEventArgs e)
        {
                pred?.Stop();
        }

        private void listboxClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedImages.Clear();
            ClassName selected = listboxClasses.SelectedItem as ClassName;
            if (selected == null) return;
            foreach (var t in images)
            {
                if (t.Label == selected.Label)
                {
                    selectedImages.Add(t);
                }
            }
        }

        private void Clear_DB(object sender, RoutedEventArgs e)
        {
            pred?.ClearDatabase();
        }

        private void Stats(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(pred?.DatabaseStats());
        }
    }

    public class ClassName : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Label { get; set; }
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

        public ClassName(string labelName)
        {
            Num = 1;
            Label = labelName;
        }

        public override string ToString()
        {
            return Label + ": " + Num;
        }
    }

    public class Image : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Path { get; set; }
        private string labelName;
        public string Label
        {
            get
            {
                return labelName;
            }
            set
            {
                labelName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Label"));
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
            Label = "";
        }
    }
}
