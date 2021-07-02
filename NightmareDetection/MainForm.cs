using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace NightmareDetection
{
    public partial class MainForm : Form
    {
        List<string> AvailableLogs = GetSpoolerLogs();
        List<string> HookedSpoolerLogs = new List<string>();
        List<EventRecord> Events = new List<EventRecord>();
        List<EventLogWatcher> HookedLogsWatchers = new List<EventLogWatcher>();
        private string fileNameString = string.Empty;
        public MainForm()
        {
            InitializeComponent();
        }
        private void MainForm_Load(object sender, EventArgs e)
        {
            if (IsAdministratorControl() == true)
            {
                EnableSpoolerOperationalLog();
                SmtpConfigCheck();
                StartEventLogHook(AvailableLogs);
                lblStatus.ForeColor = System.Drawing.Color.Green;
                lblStatus.Text = @"Monitoring";
                lblStatus.Font = new System.Drawing.Font(lblStatus.Font.FontFamily.Name, lblStatus.Font.Size, System.Drawing.FontStyle.Bold);
                btnMonitor.Text = @"Stop Monitoring";
            }
            else
            {
                DialogResult dialog = new DialogResult();
                dialog = MessageBox.Show(@"Please run application with Administrator rights", @"Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (dialog == DialogResult.OK)
                {
                    Application.Exit();
                }
            }
        }
        private void SendNotification()
        {
            var configFile = Application.StartupPath + "\\smtpConfig.conf";
            try
            {
                if (File.Exists(configFile))
                {
                    var config = File.ReadAllText(configFile);
                    var configString = Base64Decode(config);
                    var configStringParser = configString.Split('|');
                    txtSmtpServer.Text = configStringParser[0];
                    txtSmtpPort.Text = configStringParser[1];
                    txtSmtpUser.Text = configStringParser[2];
                    txtSmtpPassword.Text = configStringParser[3];
                    txtSmtpFromMail.Text = configStringParser[4];
                    txtSmtpToMail.Text = configStringParser[5];
                    chkSsl.Checked = Convert.ToBoolean(configStringParser[6]);
                    SendEmail(configStringParser[0], configStringParser[1], configStringParser[2], configStringParser[3], configStringParser[4], configStringParser[5], Convert.ToBoolean(configStringParser[6]));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        private void EnableSpoolerOperationalLog()
        {
            var configFile = Application.StartupPath + "\\application.conf";
            if (!File.Exists(configFile))
            {
                using (var writer = new StreamWriter(configFile))
                {
                    writer.WriteLine(@"Log=1");
                }
                var path = Application.StartupPath + "\\enableSpoolerLog.ps1";
                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine("$PrintSpoolerlog = Get-WinEvent -ListLog 'Microsoft-Windows-PrintService/Operational'");
                    writer.WriteLine("$PrintSpoolerlog.IsEnabled = $True");
                    writer.WriteLine("$PrintSpoolerlog.MaximumSizeInBytes = 2105344");
                    writer.WriteLine("$PrintSpoolerlog.SaveChanges()");
                }
                var startInfo = new ProcessStartInfo()
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy bypass -file \"{path}\"",
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);
            }

        }
        private void StartEventLogHook(List<string> logNames)
        {
            foreach (var logName in logNames)
            {
                try
                {
                    var query = new EventLogQuery(logName, PathType.LogName);
                    var ew = new EventLogWatcher(query);
                    ew.EventRecordWritten += new EventHandler<EventRecordWrittenEventArgs>(OnEntryWritten);
                    ew.Enabled = true;
                    Debug.WriteLine(@"Registred to : " + logName);
                    HookedSpoolerLogs.Add(logName);
                    HookedLogsWatchers.Add(ew);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(@"Faild Registred to : " + logName);
                }
            }
            Debug.WriteLine("Hooked Logs : ");
            foreach (var hookedLog in HookedSpoolerLogs)
            {
                Debug.WriteLine(hookedLog);
            }

        }
        private void OnEntryWritten(object sender, EventRecordWrittenEventArgs e)
        {
            var entry = e.EventRecord;
            Events.Add(entry);
            var row = (DataGridViewRow)detectionGrid.Rows[0].Clone();

            row.Cells[0].Value = entry.TimeCreated;
            row.Cells[1].Value = entry.LogName;
            row.Cells[2].Value = entry.ProviderName;
            row.Cells[3].Value = entry.Id;
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(entry.ToXml());
            var sw = new StringWriter();
            xmlDoc.Save(sw);

            row.Cells[4].Value = sw.ToString();
            row.Cells[4].ToolTipText = sw.ToString();

            detectionGrid.Invoke((MethodInvoker)delegate 
            { 
                detectionGrid.Rows.Add(row);
            });
            SearchAttackInLog();
        }
        private void SearchAttackInLog()
        {
            var searchEventIdValue = "316";
            detectionGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            try
            {
                foreach (DataGridViewRow row in detectionGrid.Rows)
                {
                    if (row.Cells[3].Value.ToString().Equals(searchEventIdValue))
                    {
                        row.Selected = true;
                        MessageBox.Show(@"PrintNightMare Attack Detected", @"Detected", MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        fileNameString = string.Empty;
                        fileNameString = DateTime.Now.Day.ToString() + "_" + DateTime.Now.Month.ToString() + "_" + DateTime.Now.Year + "_" +
                                             RandomFileName(0, 5) + ".log";
                        SaveReportasXML(Application.StartupPath + "\\" + fileNameString);
                        SendNotification();
                        break;
                    }
                }
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.Message);
            }
        }
        private void SaveReportasXML(string path)
        {
            var Results = "<Events>";
            var tr = new StreamWriter(path);
            foreach (EventRecord Event in Events)
            {
                Results += Event.ToXml();
            }
            Results += "</Events>";

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(Results);
            xmlDoc.Save(tr);
            tr.Flush();
            tr.Close();
        }
        private static List<string> GetSpoolerLogs()
        {
            var logNames = new List<string>();
            logNames.Add("Microsoft-Windows-PrintService/Operational");

            return logNames;
        }
        public void StartStopMonitoring()
        {
            foreach (var ew in HookedLogsWatchers)
            {
                if (btnMonitor.Text == @"Start Monitoring")
                {
                    ew.Enabled = true;
                    lblStatus.ForeColor = System.Drawing.Color.Green;
                    lblStatus.Text = @"Monitoring";
                    lblStatus.Font = new System.Drawing.Font(lblStatus.Font.FontFamily.Name, lblStatus.Font.Size, System.Drawing.FontStyle.Bold);
                    btnMonitor.Text = @"Stop Monitoring";
                }
                else
                {
                    ew.Enabled = !ew.Enabled;
                    lblStatus.ForeColor = System.Drawing.Color.Red;
                    lblStatus.Text = "Not Monitoring";
                    lblStatus.Font = new System.Drawing.Font(lblStatus.Font.FontFamily.Name, lblStatus.Font.Size, System.Drawing.FontStyle.Italic);
                    btnMonitor.Text = @"Start Monitoring";
                }
            }
        }
        private void btnMonitor_Click(object sender, EventArgs e)
        {
            StartStopMonitoring();
        }
        private void btnClearLog_Click(object sender, EventArgs e)
        {
            detectionGrid.Rows.Clear();
            Events.Clear();
        }
        private void btnExportLog_Click(object sender, EventArgs e)
        {
            var sd = new SaveFileDialog();
            if (sd.ShowDialog() == DialogResult.OK)
            {
                SaveReportasXML(sd.FileName);
            }
        }
        public static bool IsAdministratorControl()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        public static string RandomFileName(int start, int end)
        {
            var random = new Random();
            var array = $"0123456789".ToCharArray();
            var text = string.Empty;
            for (var i = start; i < end; i++)
            {
                text += array[random.Next(0, array.Length - 1)].ToString();
            }
            return text;
        }
        private void pictureBox1_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://twitter.com/priviasec");
        }
        private void MainForm_Resize(object sender, EventArgs e)
        {
            ShowInTaskbar = false;
            notifyIcon1.Visible = true;
            notifyIcon1.BalloonTipText = @"Priviasec - PrintNightmare Detector";
            notifyIcon1.ShowBalloonTip(1000);
        }
        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ShowInTaskbar = true;
            notifyIcon1.Visible = false;
            WindowState = FormWindowState.Normal;
        }
        private void btnSaveConfig_Click(object sender, EventArgs e)
        {
            try
            {
                var config = txtSmtpServer.Text + "|" + txtSmtpPort.Text + "|" + txtSmtpUser.Text + "|" +
                             txtSmtpPassword.Text + "|" + txtSmtpFromMail.Text + "|" + txtSmtpToMail.Text + "|" + chkSsl.Checked.ToString();
                var path = Application.StartupPath + "\\smtpConfig.conf";
                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine(Base64Encode(config));
                }

                MessageBox.Show("Configuration Saved!", "PrintNightmare Detection Tool", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }
        private void SendEmail(string server, string port, string smtpUser, string smtpPass, string from, string to, bool ssl)
        {
            var sc = new SmtpClient();
            sc.Port = Int32.Parse(port);
            sc.Host = server;
            sc.EnableSsl = ssl;
            sc.Credentials = new NetworkCredential(smtpUser, smtpPass);
            
            var mail = new MailMessage();
            mail.From = new MailAddress(from, @"PrintNightware Detection Tool");
            mail.To.Add(to);
            mail.Subject = @"PrintNightware Attack Detected!"; 
            mail.IsBodyHtml = true; 
            mail.Body = "New attack detected!";
            mail.Body += Environment.NewLine + "Machine Name: "+ Environment.MachineName;
            mail.Attachments.Add(new Attachment(Application.StartupPath + "\\" + fileNameString));
            sc.Send(mail);
        }
        private void SmtpConfigCheck()
        {
            var configFile = Application.StartupPath + "\\smtpConfig.conf";
            try
            {
                if (File.Exists(configFile))
                {
                    var config = File.ReadAllText(configFile);
                    var configString = Base64Decode(config);
                    var configStringParser = configString.Split('|');
                    txtSmtpServer.Text = configStringParser[0];
                    txtSmtpPort.Text = configStringParser[1];
                    txtSmtpUser.Text = configStringParser[2];
                    txtSmtpPassword.Text = configStringParser[3];
                    txtSmtpFromMail.Text = configStringParser[4];
                    txtSmtpToMail.Text = configStringParser[5];
                    chkSsl.Checked = Convert.ToBoolean(configStringParser[6]);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        } 
        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}
