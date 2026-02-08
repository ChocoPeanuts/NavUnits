using System.Reflection;
using KSP.Localization;

namespace NavUnits
{
    // =========================================================================================
    //  Enums & Data Classes
    // =========================================================================================

    public enum SpeedUnit
    {
        Ms,
        Kmh,
        Mph,
        Knots,
        Fts,
        Mach
    }

    public enum SpeedModeEx
    {
        Surface_TAS,
        Surface_IAS,
        Surface_EAS,
        Surface_Q,
        Vertical,
        Orbit,
        Target
    }

    public enum AutoSpeedMode
    {
        Off,
        Stock,
        Custom
    }

    // ==========================================================
    //  1. General Settings
    // ==========================================================

    public class NU_GeneralSettings : GameParameters.CustomParameterNode
    {
        public override string Title { get { return Localizer.Format("#NU_Sec_General"); } }
        public override string Section { get { return Localizer.Format("#NU_Set_Section"); } }
        public override string DisplaySection { get { return Localizer.Format("#NU_Set_Section"); } }
        public override int SectionOrder { get { return 1; } }
        public override bool HasPresets { get { return false; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }

        [GameParameters.CustomParameterUI("#NU_Set_AutoMode", toolTip = "#NU_Set_AutoMode_T")]
        public AutoSpeedMode autoSpeedMode { get; set; } = AutoSpeedMode.Custom;

        [GameParameters.CustomIntParameterUI("#NU_Set_Threshold", minValue = 50, maxValue = 150, stepSize = 5, toolTip = "#NU_Set_Threshold_T")]
        public int autoSwitchThreshold { get; set; } = 100;

        [GameParameters.CustomParameterUI("#NU_Param_NavBallSync", toolTip = "#NU_Param_NavBallSync_ToolTip")]
        public bool navBallSync { get; set; } = true;

        [GameParameters.CustomParameterUI("#NU_Param_NavBallAutoSwitch", toolTip = "#NU_Param_NavBallAutoSwitch_ToolTip")]
        public bool navBallAutoSwitch { get; set; } = true;

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            if (member.Name == "autoSwitchThreshold")
                return autoSpeedMode != AutoSpeedMode.Off;

            if (member.Name == "navBallAutoSwitch")
                return !navBallSync;

            return true;
        }

        [GameParameters.CustomStringParameterUI("", autoPersistance = true, lines = 1, toolTip = "")]
        public string uSpacer { get { return ""; } set { } }

        [GameParameters.CustomParameterUI("#NU_Set_DebugMode", toolTip = "#NU_Set_DebugMode_T")]
        public bool debugMode { get; set; } = false;
    }

    // ==========================================================
    //  2. Display Modes Settings
    // ==========================================================

    public class NU_DisplaySettings : GameParameters.CustomParameterNode
    {
        public override string Title { get { return Localizer.Format("#NU_Sec_Display"); } }
        public override string Section { get { return Localizer.Format("#NU_Set_Section"); } }
        public override string DisplaySection { get { return Localizer.Format("#NU_Set_Section"); } }
        public override int SectionOrder { get { return 2; } }
        public override bool HasPresets { get { return false; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            // Control FAR-dependent settings
            if (member.Name == "enableIAS" || member.Name == "enableEAS" || member.Name == "enableQ" || member.Name == "digitsQ")
            {
                if (FarUtils.IsFarLoaded)
                {
                    if (member.Name == "digitsQ") return enableQ;
                    return true;
                }
                else
                {
                    if (member.Name == "enableIAS") enableIAS = false;
                    if (member.Name == "enableEAS") enableEAS = false;
                    if (member.Name == "enableQ") enableQ = false;
                    return false;
                }
            }
            return true;
        }

        [GameParameters.CustomParameterUI("#NU_Set_EnableVert")]
        public bool enableVert { get; set; } = true;

        [GameParameters.CustomParameterUI("#NU_Set_EnableIAS")]
        public bool enableIAS { get; set; } = false;

        [GameParameters.CustomParameterUI("#NU_Set_EnableEAS")]
        public bool enableEAS { get; set; } = false;

        [GameParameters.CustomParameterUI("#NU_Set_EnableQ")]
        public bool enableQ { get; set; } = false;

        [GameParameters.CustomIntParameterUI("#NU_Set_DigitsQ", minValue = 0, maxValue = 3)]
        public int digitsQ { get; set; } = 1;
    }

    // ==========================================================
    //  3. Unit Configuration Settings
    // ==========================================================

    public class NU_UnitSettings : GameParameters.CustomParameterNode
    {
        public override string Title { get { return Localizer.Format("#NU_Sec_Units"); } }
        public override string Section { get { return Localizer.Format("#NU_Set_Section"); } }
        public override string DisplaySection { get { return Localizer.Format("#NU_Set_Section"); } }
        public override int SectionOrder { get { return 3; } }
        public override bool HasPresets { get { return false; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }

        public int defaultUnitIndex = 0;

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            // Lock default unit selection if the unit is disabled
            if (member.Name == "isDefaultMs") return _enableMs;
            if (member.Name == "isDefaultKmh") return _enableKmh;
            if (member.Name == "isDefaultMph") return _enableMph;
            if (member.Name == "isDefaultKnots") return _enableKnots;
            if (member.Name == "isDefaultFts") return _enableFts;

            // Lock digit settings if the unit is disabled
            if (member.Name == "digitsMs") return _enableMs;
            if (member.Name == "digitsKmh") return _enableKmh;
            if (member.Name == "digitsMph") return _enableMph;
            if (member.Name == "digitsKnots") return _enableKnots;
            if (member.Name == "digitsFts") return _enableFts;
            if (member.Name == "digitsMach") return _enableMach;

            return true;
        }

        // ----------------------------------------------------
        //  Default Unit Selection
        // ----------------------------------------------------

        [GameParameters.CustomParameterUI("#NU_Set_Default_ms")]
        public bool isDefaultMs
        {
            get { return defaultUnitIndex == 0; }
            set { if (value && _enableMs) defaultUnitIndex = 0; }
        }

        [GameParameters.CustomParameterUI("#NU_Set_Default_kmh")]
        public bool isDefaultKmh
        {
            get { return defaultUnitIndex == 1; }
            set { if (value && _enableKmh) defaultUnitIndex = 1; }
        }

        [GameParameters.CustomParameterUI("#NU_Set_Default_mph")]
        public bool isDefaultMph
        {
            get { return defaultUnitIndex == 2; }
            set { if (value && _enableMph) defaultUnitIndex = 2; }
        }

        [GameParameters.CustomParameterUI("#NU_Set_Default_knots")]
        public bool isDefaultKnots
        {
            get { return defaultUnitIndex == 3; }
            set { if (value && _enableKnots) defaultUnitIndex = 3; }
        }

        [GameParameters.CustomParameterUI("#NU_Set_Default_fts")]
        public bool isDefaultFts
        {
            get { return defaultUnitIndex == 4; }
            set { if (value && _enableFts) defaultUnitIndex = 4; }
        }

        // ----------------------------------------------------
        //  Unit Enable & Digits Settings
        // ----------------------------------------------------

        // Helper to count enabled standard units (excluding Mach)
        private int CountStandardEnabled()
        {
            int count = 0;
            if (_enableMs) count++;
            if (_enableKmh) count++;
            if (_enableMph) count++;
            if (_enableKnots) count++;
            if (_enableFts) count++;
            return count;
        }

        // Ensures the default unit index points to a valid enabled unit
        private void EnsureValidDefault()
        {
            if (defaultUnitIndex == 0 && _enableMs) return;
            if (defaultUnitIndex == 1 && _enableKmh) return;
            if (defaultUnitIndex == 2 && _enableMph) return;
            if (defaultUnitIndex == 3 && _enableKnots) return;
            if (defaultUnitIndex == 4 && _enableFts) return;

            // Fallback strategy: pick first enabled
            if (_enableMs) defaultUnitIndex = 0;
            else if (_enableKmh) defaultUnitIndex = 1;
            else if (_enableMph) defaultUnitIndex = 2;
            else if (_enableKnots) defaultUnitIndex = 3;
            else if (_enableFts) defaultUnitIndex = 4;
            else defaultUnitIndex = 0;
        }

        private void ShowErrorLast()
        {
            ScreenMessages.PostScreenMessage(Localizer.Format("#NU_Error_LastUnit"), 3f, ScreenMessageStyle.UPPER_CENTER);
        }

        // Backing fields
        private bool _enableMs = true;
        private bool _enableKmh = true;
        private bool _enableMph = false;
        private bool _enableKnots = false;
        private bool _enableFts = false;
        private bool _enableMach = true;

        [GameParameters.CustomParameterUI("#NU_Set_Enable_ms")]
        public bool enableMs
        {
            get { return _enableMs; }
            set
            {
                if (!value && CountStandardEnabled() <= 1 && _enableMs) { ShowErrorLast(); return; }
                _enableMs = value;
                EnsureValidDefault();
            }
        }
        [GameParameters.CustomIntParameterUI("#NU_Set_Digits_ms", minValue = 0, maxValue = 3)]
        public int digitsMs { get; set; } = 1;

        [GameParameters.CustomParameterUI("#NU_Set_Enable_kmh")]
        public bool enableKmh
        {
            get { return _enableKmh; }
            set
            {
                if (!value && CountStandardEnabled() <= 1 && _enableKmh) { ShowErrorLast(); return; }
                _enableKmh = value;
                EnsureValidDefault();
            }
        }
        [GameParameters.CustomIntParameterUI("#NU_Set_Digits_kmh", minValue = 0, maxValue = 3)]
        public int digitsKmh { get; set; } = 0;

        [GameParameters.CustomParameterUI("#NU_Set_Enable_mph")]
        public bool enableMph
        {
            get { return _enableMph; }
            set
            {
                if (!value && CountStandardEnabled() <= 1 && _enableMph) { ShowErrorLast(); return; }
                _enableMph = value;
                EnsureValidDefault();
            }
        }
        [GameParameters.CustomIntParameterUI("#NU_Set_Digits_mph", minValue = 0, maxValue = 3)]
        public int digitsMph { get; set; } = 0;

        [GameParameters.CustomParameterUI("#NU_Set_Enable_knots")]
        public bool enableKnots
        {
            get { return _enableKnots; }
            set
            {
                if (!value && CountStandardEnabled() <= 1 && _enableKnots) { ShowErrorLast(); return; }
                _enableKnots = value;
                EnsureValidDefault();
            }
        }
        [GameParameters.CustomIntParameterUI("#NU_Set_Digits_knots", minValue = 0, maxValue = 3)]
        public int digitsKnots { get; set; } = 0;

        [GameParameters.CustomParameterUI("#NU_Set_Enable_fts")]
        public bool enableFts
        {
            get { return _enableFts; }
            set
            {
                if (!value && CountStandardEnabled() <= 1 && _enableFts) { ShowErrorLast(); return; }
                _enableFts = value;
                EnsureValidDefault();
            }
        }
        [GameParameters.CustomIntParameterUI("#NU_Set_Digits_fts", minValue = 0, maxValue = 3)]
        public int digitsFts { get; set; } = 1;

        [GameParameters.CustomParameterUI("#NU_Set_Enable_mach")]
        public bool enableMach
        {
            get { return _enableMach; }
            set { _enableMach = value; }
        }
        [GameParameters.CustomIntParameterUI("#NU_Set_Digits_mach", minValue = 0, maxValue = 3)]
        public int digitsMach { get; set; } = 2;
    }
}
