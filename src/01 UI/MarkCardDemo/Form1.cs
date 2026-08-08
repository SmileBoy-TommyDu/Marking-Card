using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.DTO;
using DrSoft.MarkCard.Impl;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.DTO;
using DrSoft.MarkCard.RTC;
using DrSoft.MarkCard.RTC.CommandProcessors;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using ComboBox = System.Windows.Forms.ComboBox;



namespace MarkingCardWinForm
{
    public partial class Form1 : Form
    {
        //private IMarkCard rtc = new RTC6Adapter();

        private uint cardNo = 1;
        private uint head = 1;

        private IMarkController markController;



        public Form1()
        {

            InitializeComponent();

            string jsonPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");
            if (!File.Exists(jsonPath))
            {
                var assembly = typeof(IMarkCardAdapter).Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("config_template.json", StringComparison.OrdinalIgnoreCase));

                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    using var reader = new StreamReader(stream);
                    File.WriteAllText(jsonPath, reader.ReadToEnd());
                }
                else
                {
                    MessageBox.Show("未找到默认配置文件，可用资源: " + string.Join(", ", assembly.GetManifestResourceNames()));
                }
            }



            Log.Logger = new LoggerConfiguration()
           .MinimumLevel.Debug()
           .Enrich.FromLogContext()
           .WriteTo.Map(
               keySelector: logEvent => logEvent.Level,
               configure: (level, wt) => wt.File(
                   path: $"logs/{level}/log-.txt",
                   rollingInterval: RollingInterval.Day,
                   outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                   retainedFileCountLimit: 30)
           )
           .WriteTo.Console()
           .CreateLogger();

            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(jsonPath));

            Logger<RTC6Adapter> logger = new Logger<RTC6Adapter>(new LoggerFactory().AddSerilog());
            IMarkCardAdapter markCard = new RTC6Adapter(logger, config);

            markController = new MarkController(markCard, new Logger<MarkController>(new LoggerFactory().AddSerilog()));
            markController.OnMarkingEnd += (uint cardNo, MarkingState state) =>
            {
                MessageBox.Show("打标完成");
            };

        }

        private void Rtc_OnError(int arg1, string arg2)
        {
            MessageBox.Show($" 打标卡{arg1}异常，{arg2}");
        }

        private void Rtc_OnMarkingEnd(int card, int state)
        {
            this.Invoke(new Action(() =>
            {
                //this.textBox26.Text = rtc.GetRealExecTime(cardNo).ToString();
                MessageBox.Show($"Card {card} Marking End");
            }));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                var result = markController.Initialize();
                if (result == MarkErrorCode.None)
                {
                    MessageBox.Show("RTC6 Initialized Successfully!");
                }
                else
                {
                    //获取MarkErrorCode枚举Description特性值

                    MessageBox.Show("RTC6 Initialization Failed!原因：" + GetEnumDescription(result));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                var errCode = markController.LaserOn();
                if (errCode != MarkErrorCode.None)
                {

                    MessageBox.Show(GetEnumDescription(errCode));
                }
                else
                {
                    MessageBox.Show("执行成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }



        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                var errCode = markController.LaserOff();
                if (errCode != MarkErrorCode.None)
                {

                    MessageBox.Show(GetEnumDescription(errCode));
                }
                else
                {
                    MessageBox.Show("执行成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        double[] targetX;
        double[] targetY;

        private void button5_Click(object sender, EventArgs e)
        {


            ProcessParam process = new ProcessParam();

            MarkingJobDto d = new MarkingJobDto();
            //todo WJ
            var line1 = new SimpleLineShapeData
            {
                UId = 1,
                Name = "Line",
                OutlinePoints = new[] { (0f, 0f), (10f, 10f) }
            };
            d.Shapes = new List<IShapeData> { line1 };
            d.ParameterMap.Add(line1.UId, process);

            markController.LoadMarkData(1, d);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            try
            {
                var errCode = markController.StartMarking();
                if (errCode != MarkErrorCode.None)
                {

                    MessageBox.Show(GetEnumDescription(errCode));
                }
                else
                {
                    MessageBox.Show("执行成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {

            try
            {

                head = 1;



                if (markController.GetMarkingState(cardNo, out MarkingState state) == MarkErrorCode.None)
                {
                    this.checkBox1.Checked = state == MarkingState.Ready;
                }

                if (markController.GetMarkingState(cardNo, out MarkingState state2) == MarkErrorCode.None)
                {
                    this.checkBox2.Checked = state2 == MarkingState.Marking;
                }


                if (markController.GetMarkingState(cardNo, out MarkingState state3) == MarkErrorCode.None)
                {
                    this.checkBox3.Checked = state3 == MarkingState.MarkEnd;
                }

                if (markController.GetScannerConnect(cardNo, (uint)head, out bool scannerConnect) == MarkErrorCode.None)
                {
                    label9.Text = scannerConnect ? "Connected" : "Not Connected";
                }



                if (markController.GetScannerTemperature(cardNo, (uint)head, out double temp1, out double temp2) == MarkErrorCode.None)
                {
                    textBox7.Text = temp1.ToString("F2") + " °C";


                    textBox8.Text = temp2.ToString("F2") + " °C";
                }



                if (markController.GetMarkingMode(cardNo, out MarkingMode markingState) == MarkErrorCode.None)
                {

                    label30.Text = markingState == MarkingMode.SoftwareMode ? "软件模式" : "IO模式";
                }


                if (markController.GetScannerPosion(cardNo, (uint)head, out PointF pos) == MarkErrorCode.None)
                {
                    textBox9.Text = pos.X.ToString("F2");
                    textBox10.Text = pos.Y.ToString("F2");
                }


            }
            catch (Exception ex)
            {

            }

        }

        private string currentCalibrationFile = string.Empty;

        private void button7_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Calibration Files (*.ct5)|*.ct5|All Files (*.*)|*.*",
                Title = "Select Calibration File"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                currentCalibrationFile = openFileDialog.FileName;

                markController.LoadCalibrationFile((uint)cardNo, currentCalibrationFile, null);
                this.textBox1.Text = currentCalibrationFile;
                MessageBox.Show("Calibration file loaded successfully.");
            }
        }

        public bool AllDigit(string str)
        {
            if (str == null)
            {
                return false;
            }
            return Regex.IsMatch(str, @"^^[+-]?\d+(\.\d+)?$");
        }

        private void button8_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Filter = "txt文件(*.txt)|*.txt|csv文件(*.csv)|*.csv";
            List<PointF> points2 = new List<PointF>();
            if (openFile.ShowDialog() == DialogResult.OK)
            {
                string filePathWithFileName = openFile.FileName;
                string[] lines;
                try
                {
                    string[] templines = File.ReadAllLines(filePathWithFileName);

                    lines = templines.Where(x => !x.Contains("BEGIN") && !x.Contains("END")).ToArray();
                    if (lines.Length < 81)
                    {
                        MessageBox.Show("txt行数需要大于81");
                        return;
                    }



                }
                catch (Exception ex)
                {
                    MessageBox.Show("读取异常");
                    return;
                }

                try
                {


                    for (int i = 0; i < lines.Count(); i++)
                    {
                        if (openFile.FileName.Contains("csv"))
                        {

                            var value = lines[i].Split('\t');

                            if (value.Length != 2)
                            {
                                MessageBox.Show($"读取{i + 1}行异常");
                                return;
                            }
                            if (!AllDigit(value[0]) || !AllDigit(value[1]))
                            {
                                MessageBox.Show($"数据行{i + 1}中有非数字，请检查！");
                                return;
                            }


                            points2.Add(new PointF(Convert.ToSingle(value[0]), Convert.ToSingle(value[1])));
                        }

                    }


                }
                catch (Exception ex)
                {



                    return;
                }
                DialogResult dialogResult = MessageBox.Show($"是否将execl导入的数据当作校准数据,直接生成校正文件?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                try
                {
                    if (dialogResult == DialogResult.OK)
                    {
                        double[] realsx = points2.Select(p => (double)p.X).ToArray();
                        double[] realsy = points2.Select(p => (double)p.Y).ToArray();

                        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "calibration_files");

                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                        }
                        string calibrationFilePath = Path.Combine(path, DateTime.Now.ToString("yyyyMMddHHmmss") + ".ct5");
                        markController.CreateCalibrationFile(currentCalibrationFile, calibrationFilePath, targetX, targetY, realsx, realsy);


                        //生成Confirm Dialog，提示是否加载校正文件
                        DialogResult loadDialogResult = MessageBox.Show($"校正文件已生成，是否加载校正文件？", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                        if (loadDialogResult == DialogResult.OK)
                        {
                            markController.LoadCalibrationFile((uint)cardNo, calibrationFilePath, null);
                            this.textBox1.Text = calibrationFilePath;
                            MessageBox.Show("Calibration file loaded successfully.");
                        }

                    }
                    else
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }
        }



        private void button12_Click(object sender, EventArgs e)
        {
            string text1 = textBox2.Text;
            string text = textBox5.Text;
            if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text1))
            {
                if (!AllDigit(text) || !AllDigit(text1))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                int angle = Convert.ToInt32(text);
                int laserOnDelay = Convert.ToInt32(text1);
                try
                {
                    var result = markController.SetLaserDelay(cardNo, angle, laserOnDelay);
                    if (result == MarkErrorCode.None)
                    {
                        MessageBox.Show("设置激光延时成功");
                    }
                    else
                    {
                        MessageBox.Show("设置激光延时失败，原因：" + GetEnumDescription(result));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void button13_Click(object sender, EventArgs e)
        {
            string text = textBox6.Text;
            if (!string.IsNullOrEmpty(text))
            {
                if (!AllDigit(text))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                double angle = Convert.ToDouble(text);
                try
                {
                  var errCode = markController.SetLaserPower(cardNo, angle);
                    if (errCode == MarkErrorCode.None)
                    {
                        MessageBox.Show("设置激光功率成功");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void button10_Click(object sender, EventArgs e)
        {
            string text = textBox3.Text;
            if (!string.IsNullOrEmpty(text))
            {
                if (!AllDigit(text))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                try
                {
                    double angle = Convert.ToDouble(text);
                    var code = markController.SetLaserFrequency(cardNo, angle);
                    if ( code==MarkErrorCode.None)
                    {
                        MessageBox.Show("设置激光频率成功");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void button11_Click(object sender, EventArgs e)
        {
            string text = textBox4.Text;
            string text1 = textBox3.Text;
            if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text1))
            {
                if (!AllDigit(text) || !AllDigit(text1))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }
                try
                {
                    double angle = Convert.ToDouble(text);
                    double pulseWidth = Convert.ToDouble(text1);
                    var code = markController.SetLaserFrequencyAndPulseWidth(cardNo, angle, pulseWidth);
                    if(code == MarkErrorCode.None)
                    {
                        MessageBox.Show("设置激光频率和脉宽成功");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }


        private void button14_Click(object sender, EventArgs e)
        {
            string text = textBox11.Text;
            string text2 = textBox12.Text;
            string text3 = textBox3.Text;
            if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2) && !string.IsNullOrEmpty(text3))
            {
                if (!AllDigit(text) || !AllDigit(text2))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                double offsetX = Convert.ToDouble(text);
                double offsetY = Convert.ToDouble(text2);
                double angle = Convert.ToDouble(text3);
                try
                {
                    var code = markController.SetOffset(cardNo, 1u, offsetX, offsetY, angle);
                    if (code == MarkErrorCode.None)
                    {
                        MessageBox.Show("设置坐标偏移成功");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void button16_Click(object sender, EventArgs e)
        {
            double degrees = 30; // 旋转角度，单位为度
            double radians = degrees * Math.PI / 180.0; // 将角度转换为弧度
            double m00 = Math.Cos(radians);
            double m01 =-Math.Sin(radians);
            double m10 = Math.Sin(radians);
            double m11 = Math.Cos(radians);
            markController.SetTransformMatrix(cardNo, 1, (float)m00, (float)m01, (float)m10, (float)m11);

            //markController.SetTransformMatrix(cardNo, 1, 1,0,0,1);

            ProcessParam process = new ProcessParam();
            //todo WJ
            MarkingJobDto d = new MarkingJobDto();

            var lineCoords = new (float x1, float y1, float x2, float y2)[]
            {
                (-25, 25, 25, 25), (25, 25, 25, -25), (25, -25, -25, -25),
                (-25, -25, -25, 25), (0, -25, 0, 25), (-25, 0, 25, 0)
            };
            var shapes = new List<IShapeData>();
            int uid = 0;
            foreach (var l in lineCoords)
            {
                uid++;
                shapes.Add(new SimpleLineShapeData
                {
                    UId = uid,
                    OutlinePoints = new[] { (l.x1, l.y1), (l.x2, l.y2) }
                });
            }

            d.Shapes = shapes;
            d.ParameterMap = shapes.ToDictionary(s => s.UId, _ => process);

            markController.LoadMarkData(1, d);



        }

        private void button18_Click(object sender, EventArgs e)
        {
            //SpiralObject spiral = new SpiralObject();
            //spiral.X = Convert.ToDouble(this.textBox15.Text);
            //spiral.Y = Convert.ToDouble(this.textBox14.Text);

            //spiral.InnerRadius = Convert.ToDouble(this.textBox17.Text);
            //spiral.OuterRadius = Convert.ToDouble(this.textBox16.Text);
            //spiral.Revolutions = Convert.ToInt32(this.textBox18.Text);
            //spiral.RotationStep = Convert.ToInt32(this.textBox19.Text);

            //Layer layer = new Layer
            //{
            //    Name = "TestLayer",

            //    Objects = new List<BaseDrawObject>() { spiral}

            //};

            //var condition = new Condition()
            //{
            //    MarkSpeed = 1,
            //    JumpSpeed = 1,
            //    DotDuration = 10,
            //    Enable = true,
            //    Power = 50,
            //    Frequency = 100,
            //    Pulse = 6,
            //    RefPower = 10,
            //    MaskNo = 0,
            //    Shot = 1,
            //    MarkDelay = 10,
            //    JumpDelay = 1000,
            //    PolyDelay = 100,
            //    LaserOnDelay = 1200,
            //    LaserOffDelay = 1250
            //};
            //try { 
            //rtc.DownloadGalvoData(cardNo, condition, layer);
            //rtc.StartMarking();
            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show(ex.Message);
            //}
        }

        private void button17_Click(object sender, EventArgs e)
        {
            //ConcentricCircleObject concentricCircle = new ConcentricCircleObject();
            //concentricCircle.X = Convert.ToDouble(this.textBox23.Text);
            //concentricCircle.Y = Convert.ToDouble(this.textBox22.Text);
            //concentricCircle.R = Convert.ToDouble(this.textBox21.Text);
            //concentricCircle.Spacing = Convert.ToDouble(this.textBox20.Text);

            //Layer layer = new Layer
            //{
            //    Name = "TestLayer",

            //    Objects = new List<BaseDrawObject>() { concentricCircle }

            //};

            //var condition = new Condition()
            //{
            //    MarkSpeed = 1,
            //    JumpSpeed = 1,
            //    DotDuration = 10,
            //    Enable = true,
            //    Power = 50,
            //    Frequency = 100,
            //    Pulse = 6,
            //    RefPower = 10,
            //    MaskNo = 0,
            //    Shot = 1,
            //    MarkDelay = 100,
            //    JumpDelay = 1000,
            //    PolyDelay = 100,
            //    LaserOnDelay = 1200,
            //    LaserOffDelay = 1250
            //};
            //try { 
            //rtc.DownloadGalvoData(cardNo, condition, layer);
            //rtc.StartMarking();
            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show(ex.Message);
            //}
        }

        private void button20_Click(object sender, EventArgs e)
        {
            ProcessParam process = new ProcessParam();

            MarkingJobDto d = new MarkingJobDto();
            //todo WJ
            var line2 = new SimpleLineShapeData
            {
                UId = 1,
                Name = "Line",
                OutlinePoints = new[] { (0f, 0f), (10f, 10f) }
            };
            d.Shapes = new List<IShapeData> { line2 };
            d.ParameterMap = new Dictionary<int, ProcessParam>();
            d.ParameterMap.Add(line2.UId, process);

            markController.LoadMarkData(1, d);
        }

        private void button19_Click(object sender, EventArgs e)
        {
            try
            {
                var errCode = markController.StartMarking();
                if (errCode != MarkErrorCode.None)
                {
                    MessageBox.Show(GetEnumDescription(errCode));
                }
                else
                {
                    MessageBox.Show("下发打标成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        bool isPaused = false;
        private void button21_Click(object sender, EventArgs e)
        {
            /*
            try { 
            if (!isPaused)
            {
                rtc.Pause(cardNo);
                isPaused = true;
                button21.Text = "恢复打标";
            }
            else
            {
                rtc.Resume(cardNo);
                isPaused = false;
                button21.Text = "暂停打标";
            }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            */
        }

        private void button22_Click(object sender, EventArgs e)
        {
            /*
            try { 
            rtc.StopMarking(cardNo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }*/
        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            
            try { 
            var combox = sender as System.Windows.Forms.ComboBox;
            if (combox.SelectedIndex == 0)
            {
                markController.SetMarkingMode(MarkingMode.IOMode);
            }
            else if (combox.SelectedIndex == 1)
            {
                 markController.SetMarkingMode(MarkingMode.SoftwareMode);
            }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            var combox = sender as ComboBox;
            cardNo = (uint)combox.SelectedIndex + 1;
        }

        private void button15_Click(object sender, EventArgs e)
        {
            /*
            string text = textBox13.Text;

            if (!string.IsNullOrEmpty(text))
            {
                if (!AllDigit(text) )
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                try { 
                double offsetX = Convert.ToDouble(text);
                rtc.SetScannerTransformAngle(cardNo, 1u, offsetX);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            } */

        }

        //Condition dCondition = new Condition()
        //{
        //    MarkSpeed = 1,
        //    JumpSpeed = 1,
        //    DotDuration = 20,
        //    Enable = true,
        //    Power = 30,
        //    Frequency = 400,
        //    Pulse = 6,
        //    RefPower = 10,
        //    MaskNo = 0,
        //    Shot = 1,
        //    MarkDelay = 100,
        //    JumpDelay = 1000,
        //    PolyDelay = 100,
        //    LaserOnDelay = 1200,
        //    LaserOffDelay = 1250
        //};

 

        private void button24_Click(object sender, EventArgs e)
        {
            string s = textBox28.Text;
            string a = textBox27.Text;

            if (!string.IsNullOrEmpty(s)&&!string.IsNullOrEmpty(a))
            {
                if (!AllDigit(s)||!AllDigit(a))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }
                try
                {
                    double markSpeed = Convert.ToDouble(s);
                    double value = Convert.ToDouble(s);
                   var errCode = markController.SetScannerSpeed(cardNo,markSpeed, value);
                    if (errCode == 0)
                    {
                        MessageBox.Show("设置扫描速度成功");
                    }
                    //dCondition.JumpSpeed = value;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void button25_Click(object sender, EventArgs e)
        {
            string s = textBox29.Text;

            if (!string.IsNullOrEmpty(s))
            {
                if (!AllDigit(s))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                int value = Convert.ToInt32(s);
                //markController.SetScannerMarkDelay(cardNo, value);
                //dCondition.MarkDelay = value;
            }
        }

        private void button26_Click(object sender, EventArgs e)
        {
            string s = textBox30.Text;

            if (!string.IsNullOrEmpty(s))
            {
                if (!AllDigit(s))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                int value = Convert.ToInt32(s);
                //rtc.SetScannerJumpDelay(cardNo, value);
                //dCondition.JumpDelay = value;
            }
        }

        private void button27_Click(object sender, EventArgs e)
        {
            string s = textBox31.Text;
            string a = textBox29.Text;
            string b = textBox30.Text;

            if (!string.IsNullOrEmpty(s)&& !string.IsNullOrEmpty(a)&& !string.IsNullOrEmpty(b))
            {
                if (!AllDigit(s)||!AllDigit(a)||!AllDigit(b))
                {
                    MessageBox.Show("请输入数字");
                    return;
                }

                int value = Convert.ToInt32(s);
                int value1 = Convert.ToInt32(a);
                int value2 = Convert.ToInt32(b);
                var errCode = markController.SetScannerDelay(cardNo, value1, value2, value);
                if (errCode == 0)
                {
                    MessageBox.Show("设置扫描延时成功");
                }
                //rtc.SetScannerPolygonDelay(cardNo, value);
                //dCondition.PolyDelay = value;
            }
            else
            {
                MessageBox.Show("请输入多边形延时和打标延时、跳转延时");
            }
        }

        private static string GetEnumDescription(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = field?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            return attribute?.Description ?? value.ToString();
        }
    }
}
