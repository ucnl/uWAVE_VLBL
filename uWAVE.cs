﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uWAVE_VLBL
{
    #region Custom enums

    public enum RC_CODES_Enum
    {
        RC_PING = 0,
        RC_PONG = 1,
        RC_DPT_GET = 2,
        RC_TMP_GET = 3,
        RC_BAT_V_GET = 4,
        RC_ERR_NSUP = 5,
        RC_ACK = 6,
        RC_USR_CMD_000 = 7,
        RC_USR_CMD_001 = 8,
        RC_USR_CMD_002 = 9,
        RC_USR_CMD_003 = 10,
        RC_USR_CMD_004 = 11,
        RC_USR_CMD_005 = 12,
        RC_USR_CMD_006 = 13,
        RC_USR_CMD_007 = 14,
        RC_USR_CMD_008 = 15,
        RC_INVALID
    }

    public enum LocalError_Enum
    {
        LOC_ERR_NO_ERROR = 0,
        LOC_ERR_INVALID_SYNTAX = 1,
        LOC_ERR_UNSUPPORTED = 2,
        LOC_ERR_TRANSMITTER_BUSY = 3,
        LOC_ERR_ARGUMENT_OUT_OF_RANGE = 4,
        LOC_ERR_INVALID_OPERATION = 5,
        LOC_ERR_UNKNOWN_FIELD_ID = 6,
        LOC_ERR_VALUE_UNAVAILIBLE = 7,
        LOC_ERR_RECEIVER_BUSY = 8,
        LOC_ERR_TX_BUFFER_OVERRUN = 9,
        LOC_ERR_CHKSUM_ERROR = 10,
        LOC_ERR_UNKNOWN
    }

    public enum LocActID_Enum
    {
        LAC_DC_INCOMING = 0,
        LAC_DC_OUTCOMING = 1,
        LAC_RC_REQUEST = 2,
        LAC_SACTION = 3,
        LAC_LC_REQUEST = 4,
        LAC_INVALID
    }

    #endregion

    public static class uWAVE
    {
        public static readonly int MaxChIDs = 28;

        public static string BCDVersionToStr(int versionData)
        {
            return string.Format("{0}.{1}", (versionData >> 0x08).ToString(), (versionData & 0xff).ToString("X2"));
        }

    }
}
