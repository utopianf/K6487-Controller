using OxyPlot;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;

namespace K6487controller
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private string fileName = null;

        private DispatcherTimer timer = new DispatcherTimer();
        private Random random = new Random();
        private int currentStep = 0;
        private int numStep = 0;

        private string temp = "";

        private Ivi.Visa.Interop.ResourceManager RM = new Ivi.Visa.Interop.ResourceManager();
        private Ivi.Visa.Interop.FormattedIO488 K6487 = new Ivi.Visa.Interop.FormattedIO488();
        private int countTrigger = 1;

        public MainWindow()
        {
            InitializeComponent();
            textIncrease.TextChanged += TextIncrease_TextChanged;
            textMeasurementTime.TextChanged += TextMeasurementTime_TextChanged;
            textSteps.TextChanged += TextSteps_TextChanged;
            
            timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (currentStep <= numStep)
            {
                K6487.WriteString("READ?");
                // wait for trigger
                System.Threading.Thread.Sleep(2000);
                temp = K6487.ReadString();

                var splitTemp = temp.Split(',');
                var current = 0.0;
                var timestamp = 0.0;
                for (int i = 0; i < splitTemp.Length / int.Parse(textTriggerCount.Text); i++)
                {
                    // Split the return string.
                    // eg. +1.040564E-06A, +2.2362990E+2, +1.380000E+2, (+123.4500)
                    //          current      timestamp      status       (voltage)
                    current += double.Parse(splitTemp[3 * i].Trim('A'));
                    timestamp += double.Parse(splitTemp[3 * i + 1]);
                }
                current = current / countTrigger;
                timestamp = timestamp / countTrigger;
                currentStep++;

                labelCurrentStep.Content = currentStep;

                // DataPoint(timestamp (min), current (A))
                Data.Add(new DataPoint(timestamp / 60.0, current));
                using (StreamWriter sw = File.AppendText(fileName))
                {
                    sw.WriteLine("{0},{1},{2}", currentStep, timestamp, current);
                }
            }
            else
            {
                K6487.WriteString("DISP:ENAB ON");
                timer.IsEnabled = false;

                K6487.IO.Close();
                System.Runtime.InteropServices.Marshal.ReleaseComObject(K6487);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(RM);

                buttonInitial.IsEnabled = true;
                buttonStart.Content = "START";
            }
        }

        private ObservableCollection<DataPoint> Data
        {
            get { return (ObservableCollection<DataPoint>)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty =
           DependencyProperty.Register("Data", typeof(ObservableCollection<DataPoint>), typeof(MainWindow), new PropertyMetadata(new ObservableCollection<DataPoint>()));

        private void TextSteps_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (textMeasurementTime.Text != "" && textSteps.Text != "")
            textIncrease.Text = (float.Parse(textMeasurementTime.Text) / (float.Parse(textSteps.Text) - 1.0)).ToString();
        }

        private void TextMeasurementTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (textMeasurementTime.Text != "" && textIncrease.Text != "")
                textSteps.Text = ((int)(float.Parse(textMeasurementTime.Text) / float.Parse(textIncrease.Text) + 1.0)).ToString();
        }

        private void TextIncrease_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (textMeasurementTime.Text != "" && textIncrease.Text != "")
                textSteps.Text = ((int)(float.Parse(textMeasurementTime.Text) / float.Parse(textIncrease.Text) + 1.0)).ToString();
        }

        private void ButtonStart_Click(object sender, RoutedEventArgs e)
        {
            if (timer.IsEnabled)
            {
                DialogResult result;
                result = System.Windows.Forms.MessageBox.Show(
                    "Really Abort?",
                    "Info",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    K6487.WriteString("ABOR");
                    timer.IsEnabled = false;
                    buttonStart.Content = "START";
                    buttonInitial.IsEnabled = true;

                    K6487.WriteString("DISP:ENAB ON");
                }
                return;
            }
            else
            {
                buttonStart.Content = "ABORT";
                buttonInitial.IsEnabled = false;
            
                // Check save directory
                string saveDirectory = Path.GetDirectoryName(buttonFile.Content.ToString());
                if (!Directory.Exists(saveDirectory))
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Invalid save directory. Please enter a valid directory then try again.",
                        "Error: Invalid Directory",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    buttonStart.Content = "START";
                    return;
                }
                
                System.Windows.Forms.MessageBox.Show(
                    "Make sure to connect the current to be measured to the picoammeter.",
                    "Start measuring",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Setup timer
                var measuretime = Convert.ToInt32(textMeasurementTime.Text);
                var increasement = Convert.ToInt32(textIncrease.Text);

                currentStep = 0;
                numStep = Convert.ToInt32(textSteps.Text);
                Data = new ObservableCollection<DataPoint>();
                
                K6487.WriteString("DISP:ENAB OFF");
                K6487.WriteString("SYST:TIME:RES");
                timer.Interval = TimeSpan.FromSeconds(increasement);
                timer.IsEnabled = true;
            }
        }

        private void ButtonFile_Click(object sender, RoutedEventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "ファイルを開く";
                openFileDialog.Filter = "全てのファイル(*.*)|*.*";
                openFileDialog.RestoreDirectory = true;
                openFileDialog.CheckFileExists = false;

                if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    fileName = openFileDialog.FileName;
                    innerButtonFile.Text = fileName;
                }
            }
        }

        private void ButtonInitial_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.MessageBox.Show(
                "Make sure NOT to connect the current to be measured to the picoammeter.",
                "Zero Check",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Connect with K6487
            try
            {
                K6487.IO = (Ivi.Visa.Interop.IMessage)RM.Open(textPort.Text);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    ex.Message,
                    "Error Opening Connection to Instrument",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                System.Runtime.InteropServices.Marshal.ReleaseComObject(K6487);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(RM);
                buttonStart.Content = "START";
                return;
            }

            // Timeout: 10s
            K6487.IO.Timeout = 10000;
            // Show the information of K6487
            K6487.WriteString("*IDN?");
            temp = K6487.ReadString();
            Title += ": " + temp;
            
            // Setup K6487
            K6487.WriteString("*RST");
            K6487.WriteString("*CLS");
            K6487.WriteString("FUNC 'CURR'");
            K6487.WriteString("SYST:ZCH ON");
            K6487.WriteString("RANG 2e-9");
            K6487.WriteString("INIT");
            K6487.WriteString("SYST:ZCOR:STAT OFF");
            K6487.WriteString("SYST:ZCOR:ACQ");
            K6487.WriteString("SYST:ZCOR ON");
            K6487.WriteString("CURR:RANG:AUTO ON");
            K6487.WriteString("SYST:ZCH OFF");
            K6487.WriteString("TRAC:TST:FORM DEL");
            // Filter
            K6487.WriteString("MED:RANK 5");
            K6487.WriteString("MED ON");
            K6487.WriteString("AVER:COUN 20");
            K6487.WriteString("AVER:TCON MOV");
            K6487.WriteString("AVER ON");
            // Trigger
            countTrigger = int.Parse(textTriggerCount.Text);
            K6487.WriteString(String.Format("TRIG:COUN {0}", countTrigger));

            var sts = 0;
            while (sts == 1)
            {
                K6487.WriteString("*OPC?");
                System.Threading.Thread.Sleep(1000);
                sts = K6487.ReadNumber();
            }

            buttonInitial.Content = "Re-initialize";
            buttonStart.IsEnabled = true;
        }
    }
}
