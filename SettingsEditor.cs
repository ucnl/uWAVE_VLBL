using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace uWAVE_VLBL
{
    public partial class SettingsEditor : Form
    {
        #region Properties

        private string gtrPortName
        {
            get
            {
                if (uPortNameCbx.SelectedItem != null)
                    return uPortNameCbx.SelectedItem.ToString();
                else
                    return string.Empty;
            }
            set
            {
                int idx = uPortNameCbx.Items.IndexOf(value);
                if (idx >= 0)
                    uPortNameCbx.SelectedIndex = idx;
            }
        }

        private string gnssEmuPortName
        {
            get
            {
                if (gnssEmuPortNameCbx.SelectedItem != null)
                    return gnssEmuPortNameCbx.SelectedItem.ToString();
                else
                    return string.Empty;
            }
            set
            {
                int idx = gnssEmuPortNameCbx.Items.IndexOf(value);
                if (idx >= 0)
                    gnssEmuPortNameCbx.SelectedIndex = idx;
            }
        }

        private bool isUseGNSSEmulation
        {
            get
            {
                return isGNSSEmulationChb.Checked;
            }
            set
            {
                isGNSSEmulationChb.Checked = value;
            }
        }

        
        private double salinity
        {
            get
            {
                return Convert.ToDouble(salinityEdit.Value);
            }
            set
            {
                decimal tmp = Convert.ToDecimal(value);
                if (tmp > salinityEdit.Maximum) tmp = salinityEdit.Maximum;
                if (tmp < salinityEdit.Minimum) tmp = salinityEdit.Minimum;
                salinityEdit.Value = tmp;
            }
        }

        private int FIFOSize
        {
            get
            {
                return Convert.ToInt32(fifoSizeEdit.Value);
            }
            set
            {
                decimal tmp = value;
                if (tmp > fifoSizeEdit.Maximum) tmp = fifoSizeEdit.Maximum;
                if (tmp < fifoSizeEdit.Minimum) tmp = fifoSizeEdit.Minimum;
                fifoSizeEdit.Value = tmp;
            }
        }

        private int BaseSize
        {
            get
            {
                return Convert.ToInt32(baseSizeEdit.Value);
            }
            set
            {
                decimal tmp = value;
                if (tmp > baseSizeEdit.Maximum) tmp = baseSizeEdit.Maximum;
                if (tmp < baseSizeEdit.Minimum) tmp = baseSizeEdit.Minimum;
                baseSizeEdit.Value = tmp;
            }

        }

        private int TargetAddr
        {
            get
            {
                return Convert.ToInt32(targetAddrEdit.Value);
            }
            set
            {
                decimal tmp = value;
                if (tmp > targetAddrEdit.Maximum) tmp = targetAddrEdit.Maximum;
                if (tmp < targetAddrEdit.Minimum) tmp = targetAddrEdit.Minimum;
            }
        }

        private double radialError
        {
            get
            {
                return Convert.ToDouble(rERrThresholdEdit.Value);
            }
            set
            {
                decimal tmp = Convert.ToDecimal(value);
                if (tmp > rERrThresholdEdit.Maximum) tmp = rERrThresholdEdit.Maximum;
                if (tmp < rERrThresholdEdit.Minimum) tmp = rERrThresholdEdit.Minimum;
                rERrThresholdEdit.Value = tmp;
            }
        }



        public SettingsContainer Value
        {
            get
            {
                SettingsContainer result = new SettingsContainer();

                result.UPortName = gtrPortName;
                result.IsGNSSEmulator = isUseGNSSEmulation;
                result.GNSSEmulatorPortName = gnssEmuPortName;
                result.Salinity = salinity;
                result.MeasurementsFIFOSize = FIFOSize;
                result.BaseSize = BaseSize;
                result.TargetAddr = TargetAddr;
                result.RadialErrorThreshold = radialError;

                return result;
            }
            set
            {
                gtrPortName = value.UPortName;
                isUseGNSSEmulation = value.IsGNSSEmulator;
                gnssEmuPortName = value.GNSSEmulatorPortName;
                salinity = value.Salinity;
                FIFOSize = value.MeasurementsFIFOSize;
                BaseSize = value.BaseSize;
                TargetAddr = value.TargetAddr;
                radialError = value.RadialErrorThreshold;
            }
        }

        #endregion


        #region Constructor

        public SettingsEditor()
        {
            InitializeComponent();

            var portNames = SerialPort.GetPortNames();

            if (portNames.Length > 0)
            {
                uPortNameCbx.Items.AddRange(portNames);
                uPortNameCbx.SelectedIndex = 0;

                gnssEmuPortNameCbx.Items.AddRange(portNames);
                gnssEmuPortNameCbx.SelectedIndex = 0;
            }

            okBtn.Enabled = CheckCtrlsValid();
        }

        #endregion

        #region Methods

        private bool CheckCtrlsValid()
        {
            return !string.IsNullOrEmpty(gtrPortName) && ((!string.IsNullOrEmpty(gnssEmuPortName)) || (!isUseGNSSEmulation));
        }


        #endregion

        #region Handlers

        private void isGNSSEmulationChb_CheckedChanged(object sender, EventArgs e)
        {
            gnssEmulatorPortNameLbl.Enabled = isGNSSEmulationChb.Checked;
            gnssEmuPortNameCbx.Enabled = isGNSSEmulationChb.Checked;

            okBtn.Enabled = CheckCtrlsValid();
        }

        private void gtrPortNameCbx_SelectedIndexChanged(object sender, EventArgs e)
        {
            okBtn.Enabled = CheckCtrlsValid();
        }

        private void gnssEmuPortNameCbx_SelectedIndexChanged(object sender, EventArgs e)
        {
            okBtn.Enabled = CheckCtrlsValid();
        }

        #endregion        
    }
}
