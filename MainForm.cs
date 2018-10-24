using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using UCNLDrivers;
using UCNLKML;
using UCNLNMEA;
using UCNLPhysics;
using UCNLUI.Dialogs;

namespace uWAVE_VLBL
{
    public partial class MainForm : Form
    {
        #region Properties

        NMEASerialPort uPort;
        NMEASerialPort gnssEmulatorPort;
        PrecisionTimer timer;
        Measurements measurements;


        string settingsFileName;
        string logPath;
        string logFileName;
        string snapshotsPath;

        TSLogProvider logger;
        SettingsProvider<SettingsContainer> settingsProvider;

        bool isAutoquery = false;
        bool isAutosnapshot = false;
        bool isRestart = false;

        bool u_isWaiting = false;
        bool u_isWaitingRemote = false;
        int u_timeoutCounter = 0;
        int u_remoteTimeoutCounter = 0;
        int u_Timeout = 2;
        int u_RemoteTimeout_S = 10;
        string u_lastQuery = string.Empty;
        bool u_settingsUpdated = false;
        bool u_ambUpdated = false;
        bool u_deviceInfoUpdated = false;

        RC_CODES_Enum u_requestID = RC_CODES_Enum.RC_INVALID;


        EventHandler<SerialErrorReceivedEventArgs> uPortErrorEventHandler;
        EventHandler<NewNMEAMessageEventArgs> uPortNewNMEAMessageEventHandler;
        EventHandler<TextAddedEventArgs> loggerTextAddedEventHandler;
        EventHandler timerTickHandler;

        AgingDouble bLatitude;
        AgingDouble bLongitude;
        AgingDouble bTemperature;
        AgingDouble bDepth;
        AgingDouble tTemperature;
        AgingDouble tDepth;
        AgingDouble bVCC;
        AgingDouble bPrs;

        AgingDouble tLatitude;
        AgingDouble tLongitude;
        AgingDouble tRadialError;
        

        List<GeoPoint3DWE> tLocation;
        GeoPoint3DWE tBestLocation;
        List<GeoPoint> bLocation;        

        delegate T NullChecker<T>(object parameter);
        NullChecker<int> intNullChecker = (x => x == null ? -1 : (int)x);
        NullChecker<double> doubleNullChecker = (x => x == null ? double.NaN : (double)x);
        NullChecker<string> stringNullChecker = (x => x == null ? string.Empty : (string)x);

        Random rnd = new Random();

        double soundSpeed = 1500;

        #endregion

        #region Constructor

        public MainForm()
        {
            InitializeComponent();

            this.Text = string.Format("{0}", Application.ProductName);

            #region file names & paths

            DateTime startTime = DateTime.Now;
            settingsFileName = Path.ChangeExtension(Application.ExecutablePath, "settings");
            logPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "LOG");
            logFileName = StrUtils.GetTimeDirTreeFileName(startTime, Application.ExecutablePath, "LOG", "log", true);
            snapshotsPath = StrUtils.GetTimeDirTree(startTime, Application.ExecutablePath, "SNAPSHOTS", false);

            #endregion

            #region logger

            loggerTextAddedEventHandler = new EventHandler<TextAddedEventArgs>(logger_TextAdded);

            logger = new TSLogProvider(logFileName);
            logger.TextAddedEvent += loggerTextAddedEventHandler;
            logger.WriteStart();            

            #endregion
           
            #region settings

            logger.Write("Loading settings...");
            settingsProvider = new SettingsProviderXML<SettingsContainer>();
            settingsProvider.isSwallowExceptions = false;

            try
            {
                settingsProvider.Load(settingsFileName);
            }
            catch (Exception ex)
            {
                ProcessException(ex, true);
            }

            logger.Write(settingsProvider.Data.ToString());

            u_RemoteTimeout_S = 3;

            #endregion

            #region NMEA
           
            NMEAParser.AddManufacturerToProprietarySentencesBase(ManufacturerCodes.UWV);
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "0", "x,x");
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "1", "x,x,x.x,x");
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "2", "x,x");

            //$PUWV3,0,2,0.00010,23.09,0.000,*12\r\n
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "3", "x,x,x.x,x.x,x.x,x.x");
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "4", "x");
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "5", "x,x.x,x.x");

            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "6", "x,x,x,x,x,x");
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "7", "x.x,x.x,x.x,x.x");

            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "?", "x");
            NMEAParser.AddProprietarySentenceDescription(ManufacturerCodes.UWV, "!", "c--c,x,c--c,x,x.x,x,x,x,x.x,x,x");


            #endregion

            #region other

            uPortErrorEventHandler = new EventHandler<SerialErrorReceivedEventArgs>(uPort_Error);
            uPortNewNMEAMessageEventHandler = new EventHandler<NewNMEAMessageEventArgs>(uPort_NewNMEAMessage);

            uPort = new NMEASerialPort(new SerialPortSettings(settingsProvider.Data.UPortName, BaudRate.baudRate9600,
                System.IO.Ports.Parity.None, DataBits.dataBits8, System.IO.Ports.StopBits.One, System.IO.Ports.Handshake.None));

            uPort.IsRawModeOnly = false;            

            timerTickHandler = new EventHandler(timer_Tick);

            timer = new PrecisionTimer();
            timer.Mode = Mode.Periodic;
            timer.Period = 1000;

            timer.Tick += timerTickHandler;
            timer.Start();


            if (settingsProvider.Data.IsGNSSEmulator)
            {
                gnssEmulatorPort = new NMEASerialPort(new SerialPortSettings(settingsProvider.Data.GNSSEmulatorPortName, BaudRate.baudRate9600,
                 Parity.None, DataBits.dataBits8, StopBits.One, Handshake.None));
            }

            measurements = new Measurements(settingsProvider.Data.MeasurementsFIFOSize, settingsProvider.Data.BaseSize);

            bLatitude = new AgingDouble("F06", "°");
            bLongitude = new AgingDouble("F06", "°");
            bTemperature = new AgingDouble("F01", "°C");
            bDepth = new AgingDouble("F03", " m");
            bPrs = new AgingDouble("F01", " mBar");
            bVCC = new AgingDouble("F01", " V");
            tTemperature = new AgingDouble("F01", "°C");
            tDepth = new AgingDouble("F02", " m");

            tLatitude = new AgingDouble("F06", "°");
            tLongitude = new AgingDouble("F06", "°");
            tRadialError = new AgingDouble("F03", " m");

            

            tLocation = new List<GeoPoint3DWE>();
            bLocation = new List<GeoPoint>();

            tBestLocation = new GeoPoint3DWE();
            tBestLocation.Latitude = double.NaN;
            tBestLocation.Longitude = double.NaN;
            tBestLocation.RadialError = double.NaN;

            marinePlot.InitTracks(settingsProvider.Data.MeasurementsFIFOSize);
            marinePlot.AddTrack("BOAT GNSS", Color.Blue, 2.0f, 2, settingsProvider.Data.MeasurementsFIFOSize, true);
            marinePlot.AddTrack("BASE", Color.Salmon, 2.0f, 8, settingsProvider.Data.BaseSize, false);            
            marinePlot.AddTrack("MEASUREMENTS", Color.Green, 2.0f, 4, settingsProvider.Data.MeasurementsFIFOSize, false);
            marinePlot.AddTrack("TARGET", Color.Black, 2.0f, 4, settingsProvider.Data.MeasurementsFIFOSize, false);
            marinePlot.AddTrack("BEST", Color.Red, 2.0f, 8, 1, false);
            
            #endregion                                    
        }

        #endregion
        
        #region Methods

        #region parsers

        private void Parse_ACK(object[] parameters)
        {
            u_isWaiting = false;

            if (!u_settingsUpdated)
                u_settingsUpdated = true;
            else if (!u_ambUpdated)
                u_ambUpdated = true;

            //#define IC_D2H_ACK              '0'        // $PUWV0,cmdID,errCode
            logger.Write(string.Format("{0} (uWAVE) >> ACK {1}", uPort.PortName, (LocalError_Enum)Enum.ToObject(typeof(LocalError_Enum), (int)parameters[1])));
        }

        private void Parse_DINFO(object[] parameters)
        {
            u_isWaiting = false;
            try
            {
                // #define IC_D2H_DINFO            '!'        
                // $PUWV!,sys_moniker,sys_version,core_moniker [release],core_version,acBaudrate,rxChID,txChID,maxChannels,sty_psu,isPTS,cfg_IsCmdMode
                string sys_moniker = parameters[0].ToString();
                string sys_version = uWAVE.BCDVersionToStr((int)parameters[1]);
                string core_moniker = parameters[2].ToString();
                string core_version = uWAVE.BCDVersionToStr((int)parameters[3]);
                double acBaudrate = (double)parameters[4];
                int rxChID = (int)parameters[5];
                int txChID = (int)parameters[6];
                int maxChannels = (int)parameters[7];                
                double styPSU = (double)parameters[8];
                int isPTSFlag = (int)parameters[9];
                int isCmdModeFlag = (int)parameters[10];
               
                u_deviceInfoUpdated = true;

                logger.Write(string.Format("{0} (uWAVE) >> DEV_INFO", uPort.PortName));

            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }
        }
           

        private void Parse_RC_RESPONSE(object[] parameters)
        {            
            u_isWaitingRemote = false;
            // #define IC_D2H_RC_RESPONSE      '3'        // $PUWV3,rcCmdID,propTime_seс,snr,[value],[azimuth]

            // $PUWV3,0,2,0.00010,23.09,0.000,*12\r\n
            
            try
            {
                int txChID = (int)parameters[0];
                double pTime = double.NaN;
                double snrd = double.NaN;
                double value = double.NaN;

                RC_CODES_Enum cmdID = (RC_CODES_Enum)(int)parameters[1];
                pTime = (double)parameters[2];
                snrd = (double)parameters[3];

                if (parameters[4] != null)
                    value = (double)parameters[4];              

                double dst = pTime * soundSpeed;
                              

                if (cmdID == RC_CODES_Enum.RC_TMP_GET)
                    tTemperature.Value = value;
                else if (cmdID == RC_CODES_Enum.RC_DPT_GET)
                    tDepth.Value = value;

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "BOAT\r\nLAT: {0}\r\nLON: {1}\r\nDPT: {2}\r\nTMP: {3}\r\nPTM: {4:F04} s\r\n",
                    bLatitude.ToString(), bLongitude.ToString(), bDepth.ToString(), bTemperature.ToString(), pTime);

                sb.AppendFormat(CultureInfo.InvariantCulture, "\r\nTARGET\r\nDST: {0:F02} m\r\nSNR: {1:F01} dB\r\n", dst, snrd);

                if (tDepth.IsInitialized && !tDepth.IsObsolete)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "DPT: {0}\r\n", tDepth.ToString());

                if (tTemperature.IsInitialized && !tTemperature.IsObsolete)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "TMP: {0}\r\n", tTemperature.ToString());
                

                if (bLatitude.IsInitialized && !bLatitude.IsObsolete &&
                    bLongitude.IsInitialized && !bLongitude.IsObsolete)
                {
                    measurements.Add(new Measurement(bLatitude.Value, bLongitude.Value, dst, snrd, bDepth.Value));

                    InvokeUpdateTrack("MEASUREMENTS", bLatitude.Value, bLongitude.Value);

                    if (measurements.IsBaseExists && tDepth.IsInitialized && (measurements.AngleRange > 270))                        
                    {
                        GeoPoint3DWE prevLocation = new GeoPoint3DWE();
                        prevLocation.Latitude = double.NaN;
                        prevLocation.Longitude = double.NaN;
                        prevLocation.Depth = tDepth.Value;
                        prevLocation.RadialError = double.NaN;                     
                       
                        double stStageRErr = 0.0;
                        int itCnt = 0;

                        var basePoints = measurements.GetBase();
                        List<PointF> basePnts = new List<PointF>();
                        foreach (var bPoint in basePoints)
                        {
                            basePnts.Add(new PointF(Convert.ToSingle(bPoint.Latitude), Convert.ToSingle(bPoint.Longitude)));
                        }

                        InvokeUpdateTrack("BASE", basePnts.ToArray());

                        var locResult = Navigation.LocateLBL_NLM(basePoints, prevLocation, settingsProvider.Data.RadialErrorThreshold, out stStageRErr, out itCnt);
                        
                        tLatitude.Value = locResult.Latitude;
                        tLongitude.Value = locResult.Longitude;
                        tRadialError.Value = locResult.RadialError;                        
                        tLocation.Add(locResult);

                        InvokeUpdateTrack("TARGET", locResult.Latitude, locResult.Longitude);

                        if (settingsProvider.Data.IsGNSSEmulator)
                        {
                            SendEMU(locResult.Latitude, locResult.Longitude, tDepth.Value, locResult.RadialError);
                        }

                        if ((double.IsNaN(tBestLocation.Latitude) || (tBestLocation.RadialError > locResult.RadialError)))                                      
                        {
                            tBestLocation.Latitude = locResult.Latitude;
                            tBestLocation.Longitude = locResult.Longitude;
                            tBestLocation.RadialError = locResult.RadialError;
                            measurements.UpdateReferencePoint(tBestLocation.Latitude, tBestLocation.Longitude);

                            InvokeUpdateTrack("BEST", tBestLocation.Latitude, tBestLocation.Longitude);
                        }                                                                     

                        InvokeSetEnabled(mainToolStrip, tracksBtn, true);
                    }
                    else
                    {
                        double cLat = 0;
                        double cLon = 0;
                        measurements.CenterOfMass(out cLat, out cLon);
                        measurements.UpdateReferencePoint(cLat, cLon);
                    }
                }


                if (tLatitude.IsInitialized && !tLatitude.IsObsolete &&
                    tLongitude.IsInitialized && !tLongitude.IsObsolete)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "LAT: {0}\r\nLON: {1}\r\n", tLatitude.ToString(), tLongitude.ToString());
                }

                if (tRadialError.IsInitialized && !tRadialError.IsObsolete)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "RER: {0}\r\n", tRadialError.ToString());

                if (!double.IsNaN(tBestLocation.RadialError))
                    sb.AppendFormat(CultureInfo.InvariantCulture, "BRE: {0:F03}\r\n", tBestLocation.RadialError);

                InvokeSetLeftUpperCornerText(sb.ToString());
                InvokeInvalidatePlot();

                if (isAutosnapshot)
                    InvokeSaveSnapShot();
            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }
        }

        private void Parse_RC_TIMEOUT(object[] parameters)
        {
            //$PUWV4,rcCmdID
            RC_CODES_Enum reqID = RC_CODES_Enum.RC_INVALID;
            int txChID = -1;

            try
            {
                txChID = (int)parameters[0];
                reqID = (RC_CODES_Enum)(int)parameters[1];
                u_OnRemotTimeout();

            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }

        }

        private void Parse_AMB_DATA(object[] parameters)
        {
            try
            {
                if (parameters[0] != null)
                {
                    bPrs.Value = (double)parameters[0];
                }

                if (parameters[1] != null)
                {
                    bTemperature.Value = (double)parameters[1];
                }

                if (parameters[2] != null)
                {
                    bDepth.Value = (double)parameters[2];
                }

                if (parameters[3] != null)
                {
                    bVCC.Value = (double)parameters[3];
                }                

                if (bPrs.IsInitialized && bTemperature.IsInitialized)
                    soundSpeed = PHX.PHX_SpeedOfSound_Calc(bTemperature.Value, bPrs.Value, settingsProvider.Data.Salinity);

            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }
        }
       

        private void Parse_RMC(object[] parameters)
        {
            DateTime tStamp = (DateTime)parameters[0];

            var latitude = doubleNullChecker(parameters[2]);
            var longitude = doubleNullChecker(parameters[4]);
            var groundSpeed = doubleNullChecker(parameters[6]);
            var courseOverGround = doubleNullChecker(parameters[7]);
            var dateTime = (DateTime)parameters[8];
            var magneticVariation = doubleNullChecker(parameters[9]);

            bool isValid = (parameters[1].ToString() != "Invalid") &&
                           (!double.IsNaN(latitude)) &&
                           (!double.IsNaN(longitude)) &&
                           (!double.IsNaN(groundSpeed)) &&
                           (!double.IsNaN(courseOverGround)) &&
                           (parameters[11].ToString() != "N");

            if (isValid)
            {
                dateTime.AddHours(tStamp.Hour);
                dateTime.AddMinutes(tStamp.Minute);
                dateTime.AddSeconds(tStamp.Second);
                dateTime.AddMilliseconds(tStamp.Millisecond);
                groundSpeed = NMEAParser.NM2Km(groundSpeed);

                if (parameters[3].ToString() == "South") latitude = -latitude;
                if (parameters[5].ToString() == "West") longitude = -longitude;


                bLatitude.Value = latitude;
                bLongitude.Value = longitude;

                GeoPoint newPoint = new GeoPoint();
                newPoint.Latitude = latitude;
                newPoint.Longitude = longitude;

                bLocation.Add(newPoint);

                InvokeUpdateTrack("BOAT GNSS", latitude, longitude);
                InvokeInvalidatePlot();

                InvokeSetEnabled(mainToolStrip, tracksBtn, true);
            }
        }

        #endregion

        #region Misc

        private void InvokeSetEnabled(ToolStrip strip, ToolStripItem item, bool enabled)
        {
            if (strip.InvokeRequired)
                strip.Invoke((MethodInvoker)delegate
                {
                    if (item.Enabled != enabled)
                        item.Enabled = enabled;
                });
            else
            {
                if (item.Enabled != enabled)
                    item.Enabled = enabled;
            }
        }

        private void InvokeInvalidatePlot()
        {
            if (marinePlot.InvokeRequired)
                marinePlot.Invoke((MethodInvoker)delegate { marinePlot.Invalidate(); });
            else
                marinePlot.Invalidate();
        }

        private void InvokeSetLeftUpperCornerText(string text)
        {
            if (marinePlot.InvokeRequired)
                marinePlot.Invoke((MethodInvoker)delegate { marinePlot.LeftUpperCornerText = text; });
            else
                marinePlot.LeftUpperCornerText = text;
        }

        private void InvokeUpdateTrack(string trackID, double lat, double lon)
        {
            if (marinePlot.InvokeRequired)
                marinePlot.Invoke((MethodInvoker)delegate { marinePlot.UpdateTrack(trackID, lat, lon); });
            else
                marinePlot.UpdateTrack(trackID, lat, lon);
        }

        private void InvokeUpdateTrack(string trackID, PointF[] pnts)
        {
            if (marinePlot.InvokeRequired)
                marinePlot.Invoke((MethodInvoker)delegate { marinePlot.UpdateTrack(trackID, pnts); });
            else
                marinePlot.UpdateTrack(trackID, pnts);
        }

        private void InvokeSaveSnapShot()
        {
            if (this.InvokeRequired)
                this.Invoke((MethodInvoker)delegate { SaveFullSnapshot(); });
            else
                SaveFullSnapshot();
        }



        private void ProcessException(Exception ex, bool isMsgBox)
        {
            string msg = logger.Write(ex);

            if (isMsgBox)
                MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void OnConnectionOpened()
        {
            u_deviceInfoUpdated = false;
            u_isWaiting = false;
            u_isWaitingRemote = false;
            u_lastQuery = string.Empty;
            u_settingsUpdated = false;
            u_ambUpdated = false;

            connectionBtn.Checked = true;
            isAutoQueryBtn.Enabled = true;
            settingsBtn.Enabled = false;
            connectionStatusLbl.Text = string.Format("CONNECTED ON {0}", uPort.PortName);            
        }

        private void OnConnectionClosed()
        {
            connectionBtn.Checked = false;
            settingsBtn.Enabled = true;
            isAutoquery = false;
            isAutoQueryBtn.Checked = false;
            isAutoQueryBtn.Enabled = false;
            connectionStatusLbl.Text = "DISCONNECTED";
        }

        private void SaveFullSnapshot()
        {
            Bitmap target = new Bitmap(this.Width, this.Height);
            this.DrawToBitmap(target, this.DisplayRectangle);

            try
            {
                if (!Directory.Exists(snapshotsPath))
                    Directory.CreateDirectory(snapshotsPath);

                target.Save(Path.Combine(snapshotsPath, string.Format("{0}.{1}", StrUtils.GetHMSString(), ImageFormat.Png)));
            }
            catch
            {
                //
            }
        }


        private void SendEMU(double tLat, double tLon, double tdpt, double tRErr)
        {
            // "hhmmss.ss,A=Valid|V=Invalid,llll.ll,N=North|S=South,yyyyy.yy,E=East|W=West,x.x,x.x,ddmmyy,x.x,a,a" },
            // $GPRMC,105552.000,A,4831.4568,N,04430.2342,E,0.17,180.99,230518,,,A*6F

            string latCardinal;
            if (tLat > 0) latCardinal = "North";
            else latCardinal = "South";

            string lonCardinal;
            if (tLon > 0) lonCardinal = "East";
            else lonCardinal = "West";

            StringBuilder sb = new StringBuilder();

            sb.Append(NMEAParser.BuildSentence(TalkerIdentifiers.GN, SentenceIdentifiers.RMC, new object[] 
            {
                DateTime.Now, 
                "Valid", 
                tLat, latCardinal,
                tLon, lonCardinal,
                null, // speed knots
                null, // track true
                DateTime.Now,
                null, // magnetic variation
                null, // magnetic variation direction
                "A",
            }));

            // "hhmmss.ss,llll.ll,a,yyyyy.yy,a,0=Fix not availible|1=GPS fix|2=DGPS fix,xx,x.x,x.x,M,x.x,M,x.x,xxxx" },
            sb.Append(NMEAParser.BuildSentence(TalkerIdentifiers.GN, SentenceIdentifiers.GGA, new object[]
            {
                DateTime.Now,
                tLat, latCardinal[0],
                tLon, lonCardinal[0],
                "GPS fix",
                settingsProvider.Data.BaseSize,
                tRErr,
                -tdpt,
                "M",
                null,
                "M",
                null,
                null
            }));

            try
            {
                gnssEmulatorPort.SendData(sb.ToString());
            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }
        }

        private void SaveTracks(string fileName)
        {
            #region Save to KML

            KMLData data = new KMLData(fileName, "Generated by RedGTR_VLBL application");
                        
            var gnssKmlTrack = new List<KMLLocation>();
            foreach (var trackItem in bLocation)
                gnssKmlTrack.Add(new KMLLocation(trackItem.Longitude, trackItem.Latitude, bDepth.Value));
            data.Add(new KMLPlacemark("BOAT GNSS", "Boat GNSS track", gnssKmlTrack.ToArray()));

            var msmsKmlTrack = new List<KMLLocation>();
            var msms = measurements.ToArray();
            foreach (var trackItem in msms)
                msmsKmlTrack.Add(new KMLLocation(trackItem.Longitude, trackItem.Latitude, bDepth.Value));
            data.Add(new KMLPlacemark("MEASUREMENT POINTS", "Measurements points", msmsKmlTrack.ToArray()));

            var targetKmlTrack = new List<KMLLocation>();
            foreach (var trackItem in tLocation)
                targetKmlTrack.Add(new KMLLocation(trackItem.Longitude, trackItem.Latitude, tDepth.Value));

            data.Add(new KMLPlacemark("TARGET", "Target track", targetKmlTrack.ToArray()));

            data.Add(new KMLPlacemark("BEST", "Location with minimal radial error", true, true, new KMLLocation(tBestLocation.Longitude, tBestLocation.Latitude, tDepth.Value)));


            try
            {
                TinyKML.Write(data, fileName);                
            }
            catch (Exception ex)
            {
                ProcessException(ex, true);
            }

            #endregion
        }

        private void AnalyzeLog(string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.Contains(">> $"))
                {
                    var nmsg = line.Substring(line.IndexOf(">>") + 3).Trim();
                    if (!nmsg.EndsWith("\r\n"))
                        nmsg = nmsg + "\r\n";


                    uPort_NewNMEAMessage(uPort, new NewNMEAMessageEventArgs(nmsg));

                    //Thread.Sleep(200);
                }
            }

        }

        #endregion

        private void u_TrySend(string msg, string queryDescription, bool isRemote)
        {
            try
            {
                uPort.SendData(msg);
                logger.Write(string.Format("{0} (uWAVE) << {1}", uPort.PortName, msg));
                u_lastQuery = queryDescription;
                u_isWaiting = true;
                u_timeoutCounter = 0;

                if (isRemote)
                {
                    u_isWaitingRemote = true;
                    u_remoteTimeoutCounter = 0;
                }
            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }            
        }

        private void u_OnTimeout()
        {
            u_isWaiting = false;
            logger.Write(string.Format("{0} (uWAVE) >> {1} timeout", uPort.PortName, u_lastQuery));
        }

        private void u_OnRemotTimeout()
        {
            u_isWaitingRemote = false;
            logger.Write(string.Format("SUB #{0} {1} timeout", settingsProvider.Data.TargetAddr, u_requestID));
        }


        private void u_QuerySettingsUpdate(int txChID, int rxChID, double salinityPSU)
        {
            var msg = NMEAParser.BuildProprietarySentence(ManufacturerCodes.UWV, "1", new object[] { txChID, rxChID, salinityPSU, 1 });
            u_TrySend(msg, "Settings update", false);
        }

        private void u_DeviceInfoQuery()
        {
            var msg = NMEAParser.BuildProprietarySentence(ManufacturerCodes.UWV, "?", new object[] { 0 });
            u_TrySend(msg, "Device info query", false);
        }

        private void u_AmbConfigUpdate()
        {
            var msg = NMEAParser.BuildProprietarySentence(ManufacturerCodes.UWV, "6", new object[] { 0, 1, 1, 1, 1, 1 });
            u_TrySend(msg, "Ambient data config", false);
        }

        private void u_QueryRemote(int targetAddr, RC_CODES_Enum cmd)
        {
            var msg = NMEAParser.BuildProprietarySentence(ManufacturerCodes.UWV, "2", new object[] { targetAddr, (int)cmd });

            u_requestID = cmd;
            u_TrySend(msg, string.Format("uWAVE << ? SUB #{0}, CMD {1}", targetAddr, cmd), true);
        }

        #endregion

        #region Handlers

        #region UI

        #region mainToolStrip

        private void connectionBtn_Click(object sender, EventArgs e)
        {
            if (uPort.IsOpen)
            {
                uPort.PortError -= uPortErrorEventHandler;
                uPort.NewNMEAMessage -= uPortNewNMEAMessageEventHandler;

                try
                {
                    uPort.Close();
                }
                catch (Exception ex)
                {
                    ProcessException(ex, true);
                }

                if (settingsProvider.Data.IsGNSSEmulator)
                {
                    try
                    {
                        gnssEmulatorPort.Close();
                    }
                    catch (Exception ex)
                    {
                        ProcessException(ex, false);
                    }
                }

                OnConnectionClosed();                
            }
            else
            {
                try
                {
                    uPort.Open();
                    uPort.PortError += uPortErrorEventHandler;
                    uPort.NewNMEAMessage += uPortNewNMEAMessageEventHandler;

                    OnConnectionOpened();
                }
                catch (Exception ex)
                {
                    ProcessException(ex, true);
                    OnConnectionClosed();
                }

                if ((uPort.IsOpen) && (settingsProvider.Data.IsGNSSEmulator))
                {
                    try
                    {
                        gnssEmulatorPort.Open();
                    }
                    catch (Exception ex)
                    {
                        ProcessException(ex, false);
                    }
                }
            }
        }

        private void isAutoQueryBtn_Click(object sender, EventArgs e)
        {
            isAutoquery = !isAutoquery;
            isAutoQueryBtn.Checked = isAutoquery;
        }

        private void isAutosnapshotBtn_Click(object sender, EventArgs e)
        {
            isAutosnapshot = !isAutosnapshot;
            isAutosnapshotBtn.Checked = isAutosnapshot;
        }

        private void exportTracksBtn_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sDialog = new SaveFileDialog())
            {
                sDialog.Title = "Exporting tracks...";
                sDialog.Filter = "Google KML (*.kml)|*.kml";
                sDialog.FileName = string.Format("{0}.kml", StrUtils.GetHMSString());
                sDialog.DefaultExt = "kml";

                if (sDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SaveTracks(sDialog.FileName);
                }
            }
        }

        private void clearTracksBtn_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Clear tracks?", "Question", MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes)
            {
                tLocation.Clear();
                bLocation.Clear();
                marinePlot.ClearTracks();
                marinePlot.Invalidate();

                tracksBtn.Enabled = false;
            }
        }

        private void analyzeBtn_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog oDialog = new OpenFileDialog())
            {
                oDialog.Title = "Select a log file to analyze...";
                oDialog.DefaultExt = "log";
                oDialog.Filter = "Log files (*.log)|*.log";
                oDialog.InitialDirectory = logPath;

                if (oDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    bool isLoaded = false;
                    string[] logLines = null;

                    try
                    {
                        logLines = File.ReadAllLines(oDialog.FileName);
                        isLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        ProcessException(ex, true);
                    }

                    if (isLoaded)
                        AnalyzeLog(logLines);
                }
            }
        }


        private void settingsBtn_Click(object sender, EventArgs e)
        {
            bool isSaved = false;

            using (SettingsEditor sDialog = new SettingsEditor())
            {
                sDialog.Text = string.Format("{0} - [Settings]", Application.ProductName);
                sDialog.Value = settingsProvider.Data;

                if (sDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    settingsProvider.Data = sDialog.Value;

                    try
                    {
                        settingsProvider.Save(settingsFileName);
                        isSaved = true;
                    }
                    catch (Exception ex)
                    {
                        ProcessException(ex, true);
                    }
                }
            }

            if (isSaved)
            {
                if (MessageBox.Show("Restart application to apply new settings?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
                {
                    isRestart = true;
                    Application.Restart();
                }
            }
        }

        private void infoBtn_Click(object sender, EventArgs e)
        {
            using (AboutBox aDialog = new AboutBox())
            {
                aDialog.ApplyAssembly(Assembly.GetExecutingAssembly());
                aDialog.Weblink = "www.unavlab.com";
                aDialog.ShowDialog();
            }
        }

        #endregion

        #region mainFrom

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            logger.TextAddedEvent -= loggerTextAddedEventHandler;
            logger.FinishLog();

            timer.Tick -= timerTickHandler;
            if (timer.IsRunning)
                timer.Stop();
            timer.Dispose();

            if (gnssEmulatorPort != null)
            {
                if (gnssEmulatorPort.IsOpen)
                    gnssEmulatorPort.Close();

                gnssEmulatorPort.Dispose();
            }

            if (uPort.IsOpen)
            {
                uPort.NewNMEAMessage -= uPortNewNMEAMessageEventHandler;
                uPort.PortError -= uPortErrorEventHandler;
                uPort.Close();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = (!isRestart) && (MessageBox.Show("Close application?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.Yes);
        }

        #endregion

        #region consoleToolStrip

        private void clearHistoryBtn_Click(object sender, EventArgs e)
        {
            historyTxb.Clear();
        }

        #endregion

        #region historyTxb

        private void historyTxb_TextChanged(object sender, EventArgs e)
        {
            historyTxb.ScrollToCaret();
        }

        #endregion

        #region plotPanel

        private void plotPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!e.ClipRectangle.IsEmpty)
            {
                #region pre-init

                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                float width = this.Width;
                float height = this.Height;

                e.Graphics.TranslateTransform(width / 2.0f, height / 2.0f);                

                #endregion



            }
        }

        #endregion

        #endregion

        #region logger

        private void logger_TextAdded(object sender, TextAddedEventArgs e)
        {
            if (historyTxb.InvokeRequired)
                historyTxb.Invoke((MethodInvoker)delegate { historyTxb.AppendText(e.Text); });
            else
                historyTxb.AppendText(e.Text);
        }

        #endregion        

        #region uPort

        private void uPort_Error(object sender, SerialErrorReceivedEventArgs e)
        {
            logger.Write(string.Format("{0} (uWAVE) >> {1}", uPort.PortName, e.EventType.ToString()));
        }

        private void uPort_NewNMEAMessage(object sender, NewNMEAMessageEventArgs e)
        {
            logger.Write(string.Format("{0} (uWAVE) >> {1}", uPort.PortName, e.Message));            

            try
            {
                var result = NMEAParser.Parse(e.Message);

                if (result is NMEAProprietarySentence)
                {
                    NMEAProprietarySentence pResult = result as NMEAProprietarySentence;

                    if (pResult.Manufacturer == ManufacturerCodes.UWV)
                    {
                        if (pResult.SentenceIDString == "0") // ACK
                            Parse_ACK(pResult.parameters);
                        else if (pResult.SentenceIDString == "!") // Device info
                            Parse_DINFO(pResult.parameters);
                        else if (pResult.SentenceIDString == "3")
                            Parse_RC_RESPONSE(pResult.parameters);
                        else if (pResult.SentenceIDString == "4")
                            Parse_RC_TIMEOUT(pResult.parameters);                        
                        else if (pResult.SentenceIDString == "7")
                            Parse_AMB_DATA(pResult.parameters);                        
                    }
                    else
                    {
                        // not supported manufacturer code
                    }
                }
                else
                {
                    NMEAStandartSentence sSentence = result as NMEAStandartSentence;

                    if (sSentence.SentenceID == SentenceIdentifiers.RMC)
                        Parse_RMC(sSentence.parameters);
                }
            }
            catch (Exception ex)
            {
                ProcessException(ex, false);
            }            
        }

        #endregion

        #region timer

        private void timer_Tick(object sender, EventArgs e)
        {
            if (u_isWaiting)
            {
                if (++u_timeoutCounter > u_Timeout)
                {
                    u_OnTimeout();
                }
            }
            else if (u_isWaitingRemote)
            {
                if (++u_remoteTimeoutCounter > u_RemoteTimeout_S)
                {
                    u_OnRemotTimeout();
                }
            }
            else
            {
                if (uPort.IsOpen)
                {
                    if (!u_deviceInfoUpdated)
                    {
                        // query device info
                        u_DeviceInfoQuery();
                    }
                    else if (!u_settingsUpdated)
                    {
                        // query salinity set
                        u_QuerySettingsUpdate(0, 0, settingsProvider.Data.Salinity);                        
                    }
                    else if (!u_ambUpdated)
                    {
                        u_AmbConfigUpdate();
                    }
                    else if (isAutoquery)
                    {
                        // query remote
                        RC_CODES_Enum cmdID = RC_CODES_Enum.RC_DPT_GET;
                        if (!bTemperature.IsInitialized || bTemperature.IsObsolete)
                            cmdID = RC_CODES_Enum.RC_TMP_GET;

                        u_QueryRemote(settingsProvider.Data.TargetAddr, cmdID);
                    }
                }
            }
        }

        #endregion                                        
        
        #endregion                
    }
}
