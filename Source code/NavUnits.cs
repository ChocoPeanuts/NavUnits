using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens.Flight;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static FlightGlobals;

namespace NavUnits
{
    // =========================================================================================
    //  Helper Components (Input Detection)
    // =========================================================================================

    public class SpeedClickDetector : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                NavUnits.Instance.CycleUnit();
            else if (eventData.button == PointerEventData.InputButton.Left)
                NavUnits.Instance.CycleSpeedMode();
        }
    }

    public class NavBallClickDetector : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                NavUnits.Instance.CycleNavBallMode();
        }
    }

    // =========================================================================================
    //  Loader Class (Config Loading)
    // =========================================================================================

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class NavUnitsLoader : MonoBehaviour
    {
        public void Awake()
        {
            NavUnits.BodyThresholds.Clear();
            if (GameDatabase.Instance == null) return;

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("NAVUNITS_BODY_CONFIG");
            NavUnits.SystemLog($"Loading Body Configs... Found {nodes.Length} config nodes.");

            foreach (ConfigNode node in nodes)
            {
                foreach (ConfigNode bodyNode in node.GetNodes("BODY"))
                {
                    string name = bodyNode.GetValue("name");
                    string altStr = bodyNode.GetValue("altitude");
                    if (!string.IsNullOrEmpty(name) && float.TryParse(altStr, out float alt))
                    {
                        if (!NavUnits.BodyThresholds.ContainsKey(name))
                        {
                            NavUnits.BodyThresholds.Add(name, alt);
                            Debug.Log($"           Config Loaded: {name} -> {alt}m");
                        }
                    }
                }
            }
        }
    }

    // =========================================================================================
    //  Main Flight Class (NavUnits)
    // =========================================================================================

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class NavUnits : MonoBehaviour
    {
        public static NavUnits Instance;

        // --- State Variables ---
        public static SpeedUnit ActiveUnit = SpeedUnit.Ms;
        public static SpeedModeEx ActiveSpeedMode = SpeedModeEx.Surface_TAS;
        public static SpeedDisplayModes ActiveNavBallMode = SpeedDisplayModes.Surface;

        private ITargetable _previousTarget;
        private bool _wasSurfaceCondition = true;

        // --- Cache Variables (Optimization) ---
        private SpeedDisplay _cachedSpeedDisplay;
        private TextMeshProUGUI _speedModeText;    // Left text (Speed Mode)
        private TextMeshProUGUI _navBallModeText;  // Right text (NavBall Mode)

        private string _cachedBodyName;
        private float _cachedRefAlt = -1f;

        public bool CachedNavBallSync { get; private set; } = true;
        public bool CachedNavBallAutoSwitch { get; private set; } = true;

        // --- Settings Cache ---
        private NU_GeneralSettings _settingsGeneral;
        private NU_DisplaySettings _settingsDisplay;
        private NU_UnitSettings _settingsUnits;

        // --- Static Data ---
        public static readonly List<SpeedUnit> UnitOrder = new List<SpeedUnit> { SpeedUnit.Ms, SpeedUnit.Kmh, SpeedUnit.Mph, SpeedUnit.Knots, SpeedUnit.Fts, SpeedUnit.Mach };
        public static Dictionary<string, float> BodyThresholds = new Dictionary<string, float>();

        // --- Constants & Render Helpers ---
        private const double M_TO_KMH = 3.6;
        private const double M_TO_MPH = 2.23693629;
        private const double M_TO_KNOTS = 1.94384449;
        private const double M_TO_FTS = 3.2808399;
        private static readonly double[] Pow10 = { 1.0, 10.0, 100.0, 1000.0, 10000.0 };
        private static readonly string[] FloatFormats = { "F0", "F1", "F2", "F3", "F4" };

        private StringBuilder _sb = new StringBuilder(64);
        private struct UnitRenderData
        {
            public double multiplier;
            public string symbol;
            public int digits;
        }
        private UnitRenderData[] _unitDataCache;

        // --- Settings Accessors ---
        public static NU_GeneralSettings General => HighLogic.CurrentGame.Parameters.CustomParams<NU_GeneralSettings>();
        public static NU_DisplaySettings Display => HighLogic.CurrentGame.Parameters.CustomParams<NU_DisplaySettings>();
        public static NU_UnitSettings Units => HighLogic.CurrentGame.Parameters.CustomParams<NU_UnitSettings>();


        // =========================================================
        //  Lifecycle Methods (Start / Destroy)
        // =========================================================

        public void Start()
        {
            Instance = this;
            SystemLog("Initialized");

            // Initialize Harmony
            var harmony = new Harmony("com.ChocoPeanuts.NavUnits");
            harmony.PatchAll();

            GameEvents.OnGameSettingsApplied.Add(OnGameSettingsApplied);

            // Initial Settings Load
            RefreshSettingsCache();

            // --- Display & UI Setup ---
            _cachedSpeedDisplay = FindObjectOfType<SpeedDisplay>();
            if (_cachedSpeedDisplay != null)
            {
                // Remove Stock Speed Mode Button logic
                var btn = _cachedSpeedDisplay.GetComponentInChildren<Button>()
                    ?? _cachedSpeedDisplay.textSpeed?.GetComponentInParent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.enabled = false;
                }

                // Setup Custom Titles
                if (_cachedSpeedDisplay.textTitle is TextMeshProUGUI stockTitle)
                {
                    stockTitle.enabled = false;
                    stockTitle.raycastTarget = false;

                    // Left Title (Speed Mode)
                    Transform tLeft = stockTitle.transform.parent.Find("NU_SpeedModeTitle");
                    if (tLeft != null)
                        _speedModeText = tLeft.GetComponent<TextMeshProUGUI>();
                    else
                    {
                        GameObject leftObj = new GameObject("NU_SpeedModeTitle");
                        leftObj.transform.SetParent(stockTitle.transform.parent, false);
                        _speedModeText = leftObj.AddComponent<TextMeshProUGUI>();
                        CopyTextStyle(stockTitle, _speedModeText, 0.8f);
                        _speedModeText.rectTransform.anchoredPosition += new Vector2(-13f, 0f);
                        _speedModeText.alignment = TextAlignmentOptions.Left;
                        _speedModeText.text = "";
                    }

                    // // Right Title (NavBall Mode)
                    Transform tRight = stockTitle.transform.parent.Find("NU_NavBallModeTitle");
                    if (tRight != null)
                        _navBallModeText = tRight.GetComponent<TextMeshProUGUI>();
                    else
                    {
                        GameObject rightObj = new GameObject("NU_NavBallModeTitle");
                        rightObj.transform.SetParent(stockTitle.transform.parent, false);
                        _navBallModeText = rightObj.AddComponent<TextMeshProUGUI>();
                        CopyTextStyle(stockTitle, _navBallModeText, 0.65f);
                        _navBallModeText.rectTransform.anchoredPosition += new Vector2(17f, 0f);
                        _navBallModeText.alignment = TextAlignmentOptions.Right;
                        _navBallModeText.text = "";
                    }
                }

                // Setup Speed Text
                if (_cachedSpeedDisplay.textSpeed is TextMeshProUGUI tmSpeed)
                {
                    tmSpeed.enableWordWrapping = false;
                    tmSpeed.fontSize *= 1.0f;
                    tmSpeed.raycastTarget = false;
                }

                // Hit Areas
                if (_cachedSpeedDisplay.transform.Find("NU_SpeedHitArea") == null)
                {
                    GameObject speedHitObj = new GameObject("NU_SpeedHitArea");
                    speedHitObj.transform.SetParent(_cachedSpeedDisplay.transform, false);
                    speedHitObj.layer = _cachedSpeedDisplay.gameObject.layer;

                    RectTransform speedRt = speedHitObj.AddComponent<RectTransform>();
                    speedRt.anchorMin = Vector2.zero;
                    speedRt.anchorMax = Vector2.one;
                    speedRt.sizeDelta = Vector2.zero;
                    speedRt.pivot = new Vector2(0.5f, 0.5f);
                    speedRt.anchoredPosition = Vector2.zero;

                    Image img = speedHitObj.AddComponent<Image>();
                    img.color = Color.clear;
                    img.raycastTarget = true;

                    speedHitObj.AddComponent<SpeedClickDetector>();
                }

                if (_cachedSpeedDisplay.transform.parent.Find("NU_NavBallHitArea") == null)
                {
                    GameObject navHitObj = new GameObject("NU_NavBallHitArea");
                    navHitObj.transform.SetParent(_cachedSpeedDisplay.transform.parent, false);
                    navHitObj.layer = _cachedSpeedDisplay.gameObject.layer;

                    RectTransform navRt = navHitObj.AddComponent<RectTransform>();
                    navRt.anchorMin = new Vector2(0.5f, 0.5f);
                    navRt.anchorMax = new Vector2(0.5f, 0.5f);
                    navRt.pivot = new Vector2(0.5f, 0.5f);
                    navRt.sizeDelta = new Vector2(220f, 160f);
                    navRt.position = _cachedSpeedDisplay.transform.position;
                    navRt.localPosition += new Vector3(0f, -110f, 0f);

                    Image image = navHitObj.AddComponent<Image>();
                    image.color = Color.clear;
                    image.raycastTarget = true;

                    navHitObj.AddComponent<NavBallClickDetector>();
                }
            }
            else
                SystemErrorLog("SpeedDisplay not found! UI modifications skipped.");

            // Disable FAR GUI to avoid overlap
            FarUtils.SetFARDisplay(false);

            // --- Initial State Calculation ---
            var vessel = FlightGlobals.ActiveVessel;
            var target = FlightGlobals.fetch.VesselTarget;
            _previousTarget = target;

            bool isSurfaceCondition = vessel != null && ShouldBeInSurfaceMode(vessel);
            _wasSurfaceCondition = isSurfaceCondition;

            // 1. Determine Unit
            ActiveUnit = GetPreferredUnit();

            // 2. Determine Speed Mode
            ActiveSpeedMode = (target != null)
                        ? SpeedModeEx.Target
                        : (isSurfaceCondition ? SpeedModeEx.Surface_TAS : SpeedModeEx.Orbit);

            // 3. Determine NavBall Mode
            ActiveNavBallMode = FlightGlobals.speedDisplayMode;

            if (target != null)
            {
                // Force Target mode if Sync or Auto is enabled
                if (CachedNavBallSync || CachedNavBallAutoSwitch)
                    ActiveNavBallMode = SpeedDisplayModes.Target;
            }
            else
            {
                // Correct NavBall if it's stuck in Target mode
                if (ActiveNavBallMode == SpeedDisplayModes.Target)
                    ActiveNavBallMode = isSurfaceCondition ? SpeedDisplayModes.Surface : SpeedDisplayModes.Orbit;
            }

            // 4. Validate and Apply
            CheckAndFixMode();
            CheckAndFixUnit();
            CheckAndFixNavBall();

            DebugLog($"Start completed. Mode: {ActiveSpeedMode}, NavBall: {ActiveNavBallMode}");
        }

        public void OnDestroy()
        {
            FarUtils.SetFARDisplay(true); // Restore FAR GUI
            GameEvents.OnGameSettingsApplied.Remove(OnGameSettingsApplied);

            // --- UI Cleanup ---
            if (_cachedSpeedDisplay != null)
            {
                if (_cachedSpeedDisplay.textTitle != null)
                    _cachedSpeedDisplay.textTitle.enabled = true;

                if (_speedModeText != null) Destroy(_speedModeText.gameObject);
                if (_navBallModeText != null) Destroy(_navBallModeText.gameObject);

                var speedHit = _cachedSpeedDisplay.transform.Find("NU_SpeedHitArea");
                if (speedHit != null) Destroy(speedHit.gameObject);

                if (_cachedSpeedDisplay.transform.parent != null)
                {
                    var navHit = _cachedSpeedDisplay.transform.parent.Find("NU_NavBallHitArea");
                    if (navHit != null) Destroy(navHit.gameObject);
                }
            }

            if (Instance == this) Instance = null;
        }

        public void OnGameSettingsApplied()
        {
            SystemLog("Game Settings Applied. Refreshing state...");

            RefreshSettingsCache();

            CheckAndFixMode();
            CheckAndFixUnit();
            CheckAndFixNavBall();
        }

        // =========================================================
        //  Core Update Logic (LateUpdate)
        // =========================================================

        public void LateUpdate()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            // Optimization: Calculate this once per frame
            bool isSurfaceCondition = ShouldBeInSurfaceMode(vessel);

            // ---------------------------------------------------------------------
            //  A. Target Monitoring & Auto-Switch Logic
            // ---------------------------------------------------------------------
            ITargetable currentTarget = FlightGlobals.fetch.VesselTarget;

            if (currentTarget != _previousTarget)
            {
                // 1. Target Acquired
                if (_previousTarget == null && currentTarget != null)
                {
                    DebugLog("Target Acquired.");

                    // Speed Display Switch
                    if (_settingsGeneral != null && _settingsGeneral.autoSpeedMode != AutoSpeedMode.Off)
                        SetSpeedMode(SpeedModeEx.Target);

                    // NavBall Switch
                    if (!CachedNavBallSync && CachedNavBallAutoSwitch)
                        SetNavBallMode(SpeedDisplayModes.Target);
                }
                // 2. Target Lost
                else if (_previousTarget != null && currentTarget == null)
                {
                    DebugLog("Target Lost.");

                    // Speed Display Revert
                    if (ActiveSpeedMode == SpeedModeEx.Target)
                        SetSpeedMode(isSurfaceCondition ? SpeedModeEx.Surface_TAS : SpeedModeEx.Orbit);

                    // NavBall Revert
                    if (ActiveNavBallMode == SpeedDisplayModes.Target)
                        SetNavBallMode(isSurfaceCondition ? SpeedDisplayModes.Surface : SpeedDisplayModes.Orbit);
                }
                _previousTarget = currentTarget;
            }

            // ---------------------------------------------------------------------
            //  B. Speed Display / NavBall Auto-Switching (Altitude based)
            // ---------------------------------------------------------------------
            if (_wasSurfaceCondition != isSurfaceCondition)
            {
                _wasSurfaceCondition = isSurfaceCondition;

                DebugLog($"Altitude threshold crossed. SurfaceCondition: {isSurfaceCondition}");

                if (_settingsGeneral != null && _settingsGeneral.autoSpeedMode != AutoSpeedMode.Off)
                {
                    if (isSurfaceCondition)
                    {
                        if (ActiveSpeedMode == SpeedModeEx.Orbit)
                            SetSpeedMode(SpeedModeEx.Surface_TAS);
                    }
                    else
                    {
                        if (IsSurfaceGroup(ActiveSpeedMode))
                            SetSpeedMode(SpeedModeEx.Orbit);
                    }
                }
                if (!CachedNavBallSync && CachedNavBallAutoSwitch)
                {
                    if (ActiveNavBallMode != SpeedDisplayModes.Target)
                    {
                        if (isSurfaceCondition)
                        {
                            if (ActiveNavBallMode == SpeedDisplayModes.Orbit)
                                SetNavBallMode(SpeedDisplayModes.Surface);
                        }
                        else
                        {
                            if (ActiveNavBallMode == SpeedDisplayModes.Surface)
                                SetNavBallMode(SpeedDisplayModes.Orbit);
                        }
                    }
                }
            }

            // ---------------------------------------------------------------------
            //  C. NavBall Sync Enforcement (Anti-Flicker)
            // ---------------------------------------------------------------------
            if (!CachedNavBallSync)
            {
                if (FlightGlobals.speedDisplayMode != ActiveNavBallMode)
                    SetNavBallMode(ActiveNavBallMode);
            }

            // ---------------------------------------------------------------------
            //  D. Rendering (Truncate Logic & Optimization)
            // ---------------------------------------------------------------------

            if (_cachedSpeedDisplay == null || _speedModeText == null) return;

            _sb.Length = 0;
            double displayValue = 0;
            int digits = 1;
            string symbol = "";

            // 1. Fetch & Setup
            switch (ActiveSpeedMode)
            {
                // --- Surface Group ---
                case SpeedModeEx.Surface_TAS:
                    displayValue = vessel.srfSpeed;
                    goto CaseCommonUnitProcessing;
                case SpeedModeEx.Surface_IAS:
                    displayValue = FarUtils.GetIAS();
                    goto CaseCommonUnitProcessing;
                case SpeedModeEx.Surface_EAS:
                    displayValue = FarUtils.GetEAS();
                    goto CaseCommonUnitProcessing;

                // --- Q (Unique handling) ---
                case SpeedModeEx.Surface_Q:
                    displayValue = FarUtils.GetQ();
                    digits = (_settingsDisplay != null) ? _settingsDisplay.digitsQ : 1;
                    symbol = " kPa";
                    break;

                // --- Other Modes ---
                case SpeedModeEx.Vertical:
                    displayValue = vessel.verticalSpeed;
                    goto CaseCommonUnitProcessing;

                case SpeedModeEx.Orbit:
                    displayValue = FlightGlobals.ship_obtSpeed;
                    goto CaseCommonUnitProcessing;

                case SpeedModeEx.Target:
                    displayValue = FlightGlobals.ship_tgtSpeed;
                    if (currentTarget != null)
                    {
                        // Calculate relative velocity direction
                        // FlightGlobals.ship_tgtVelocity is vessel relative velocity to target
                        Vector3d toTarget = currentTarget.GetTransform().position - vessel.GetTransform().position;
                        if (Vector3d.Dot(FlightGlobals.ship_tgtVelocity, toTarget) > 0)
                            displayValue *= -1;
                    }
                    goto CaseCommonUnitProcessing;

                // --- Common Unit Processing ---
                CaseCommonUnitProcessing:
                    UnitRenderData data = _unitDataCache[(int)ActiveUnit];
                    displayValue = (ActiveUnit == SpeedUnit.Mach) ? vessel.mach : displayValue * data.multiplier;
                    digits = data.digits;
                    symbol = data.symbol;
                    break;
            }

            // 2. Formatting (Truncate Logic)
            digits = Mathf.Clamp(digits, 0, FloatFormats.Length - 1);
            double factor = Pow10[digits];

            // Truncate: Remove decimals towards zero (99.9 -> 99, -0.5 -> 0)
            displayValue = System.Math.Truncate(displayValue * factor) / factor;

            // Add plus sign for Vertical/Target modes
            if (displayValue >= 0 && (ActiveSpeedMode == SpeedModeEx.Vertical || ActiveSpeedMode == SpeedModeEx.Target))
                _sb.Append('+');

            _sb.Append(displayValue.ToString(FloatFormats[digits], CultureInfo.InvariantCulture));
            _sb.Append(symbol);

            if (_cachedSpeedDisplay.textSpeed != null)
                _cachedSpeedDisplay.textSpeed.SetText(_sb);
        }

        // =========================================================
        //  Logic: Unit Management
        // =========================================================

        public void CycleUnit()
        {
            if (ActiveSpeedMode == SpeedModeEx.Surface_Q) return; // Units irrelevant for Q (kPa)

            int currentIndex = UnitOrder.IndexOf(ActiveUnit);
            if (currentIndex == -1) currentIndex = 0;

            for (int i = 1; i <= UnitOrder.Count; i++)
            {
                int nextIndex = (currentIndex + i) % UnitOrder.Count;
                SpeedUnit candidate = UnitOrder[nextIndex];
                if (IsValidUnitForCurrentMode(candidate))
                {
                    ActiveUnit = candidate;
                    DebugLog($"Unit cycled to: {ActiveUnit}");
                    return;
                }
            }
            ActiveUnit = GetPreferredUnit();
        }

        public static SpeedUnit GetPreferredUnit()
        {
            if (Instance == null || Instance._settingsUnits == null) return SpeedUnit.Ms;
            int idx = Instance._settingsUnits.defaultUnitIndex;

            if (idx >= 0 && idx < UnitOrder.Count)
            {
                SpeedUnit candidate = UnitOrder[idx];
                if (IsValidUnitForCurrentMode(candidate)) return candidate;
            }

            return SpeedUnit.Ms;
        }

        public static bool IsValidUnitForCurrentMode(SpeedUnit unit)
        {
            if (!IsUnitEnabled(unit)) return false;
            // Mach is invalid for Orbit, Target, and Vertical modes
            if (unit == SpeedUnit.Mach && (ActiveSpeedMode == SpeedModeEx.Vertical || ActiveSpeedMode == SpeedModeEx.Orbit || ActiveSpeedMode == SpeedModeEx.Target))
                return false;
            return true;
        }

        private void CheckAndFixUnit()
        {
            if (!IsValidUnitForCurrentMode(ActiveUnit))
            {   var oldUnit = ActiveUnit;
                ActiveUnit = GetPreferredUnit();
                DebugLog($"Unit fixed from {oldUnit} to {ActiveUnit}");
            }
        }

        public static bool IsUnitEnabled(SpeedUnit unit)
        {
            if (Instance == null || Instance._settingsUnits == null) return true;
            var s = Instance._settingsUnits;
            switch (unit)
            {
                case SpeedUnit.Ms: return s.enableMs;
                case SpeedUnit.Kmh: return s.enableKmh;
                case SpeedUnit.Mph: return s.enableMph;
                case SpeedUnit.Knots: return s.enableKnots;
                case SpeedUnit.Fts: return s.enableFts;
                case SpeedUnit.Mach: return s.enableMach;
                default: return false;
            }
        }

        // =========================================================
        //  Logic: Speed Mode Management
        // =========================================================

        public static void SetSpeedMode(SpeedModeEx newMode)
        {
            if (ActiveSpeedMode == newMode) return;

            DebugLog($"Set SpeedMode to: {newMode}");
            ActiveSpeedMode = newMode;
            if (Instance != null)
            {
                Instance.CheckAndFixUnit();
                Instance.UpdateSpeedTitle();
                if (Instance.CachedNavBallSync)
                {
                    ApplyKspModeSync(newMode);
                }
            }
        }

        public void CycleSpeedMode()
        {
            DebugLog($"Cycling SpeedMode.");
            switch (ActiveSpeedMode)
            {
                case SpeedModeEx.Surface_TAS:
                    if (_settingsDisplay != null && _settingsDisplay.enableIAS && FarUtils.IsFarLoaded) { SetSpeedMode(SpeedModeEx.Surface_IAS); return; }
                    if (_settingsDisplay != null && _settingsDisplay.enableEAS && FarUtils.IsFarLoaded) { SetSpeedMode(SpeedModeEx.Surface_EAS); return; }
                    if (_settingsDisplay != null && _settingsDisplay.enableQ && FarUtils.IsFarLoaded) { SetSpeedMode(SpeedModeEx.Surface_Q); return; }
                    if (TryGoToVertical()) return;
                    SetSpeedMode(SpeedModeEx.Orbit);
                    break;
                case SpeedModeEx.Surface_IAS:
                    if (_settingsDisplay != null && _settingsDisplay.enableEAS && FarUtils.IsFarLoaded) { SetSpeedMode(SpeedModeEx.Surface_EAS); return; }
                    if (_settingsDisplay != null && _settingsDisplay.enableQ && FarUtils.IsFarLoaded) { SetSpeedMode(SpeedModeEx.Surface_Q); return; }
                    if (TryGoToVertical()) return;
                    SetSpeedMode(SpeedModeEx.Orbit);
                    break;
                case SpeedModeEx.Surface_EAS:
                    if (_settingsDisplay != null && _settingsDisplay.enableQ && FarUtils.IsFarLoaded) { SetSpeedMode(SpeedModeEx.Surface_Q); return; }
                    if (TryGoToVertical()) return;
                    SetSpeedMode(SpeedModeEx.Orbit);
                    break;
                case SpeedModeEx.Surface_Q:
                    if (TryGoToVertical()) return;
                    SetSpeedMode(SpeedModeEx.Orbit);
                    break;
                case SpeedModeEx.Vertical:
                    SetSpeedMode(SpeedModeEx.Orbit);
                    break;
                case SpeedModeEx.Orbit:
                    if (FlightGlobals.fetch.VesselTarget != null) SetSpeedMode(SpeedModeEx.Target);
                    else SetSpeedMode(SpeedModeEx.Surface_TAS);
                    break;
                case SpeedModeEx.Target:
                    SetSpeedMode(SpeedModeEx.Surface_TAS);
                    break;
            }
        }

        private bool TryGoToVertical()
        {
            if (_settingsDisplay != null && _settingsDisplay.enableVert) { SetSpeedMode(SpeedModeEx.Vertical); return true; }
            return false;
        }

        private void CheckAndFixMode()
        {
            bool isModeValid = true;
            if (_settingsDisplay == null) return;

            switch (ActiveSpeedMode)
            {
                case SpeedModeEx.Surface_IAS:
                    if (!_settingsDisplay.enableIAS || !FarUtils.IsFarLoaded) isModeValid = false;
                    break;
                case SpeedModeEx.Surface_EAS:
                    if (!_settingsDisplay.enableEAS || !FarUtils.IsFarLoaded) isModeValid = false;
                    break;
                case SpeedModeEx.Surface_Q:
                    if (!_settingsDisplay.enableQ || !FarUtils.IsFarLoaded) isModeValid = false;
                    break;
                case SpeedModeEx.Vertical:
                    if (!_settingsDisplay.enableVert) isModeValid = false;
                    break;
                default: isModeValid = true; break;
            }

            if (!isModeValid)
            {
                DebugLog($"Current Mode {ActiveSpeedMode} invalid. Resetting.");
                var v = FlightGlobals.ActiveVessel;
                if (v != null && !ShouldBeInSurfaceMode(v))
                    SetSpeedMode(SpeedModeEx.Orbit);
                else
                    SetSpeedMode(SpeedModeEx.Surface_TAS);
            }
            else
                UpdateSpeedTitle();
        }

        // =========================================================
        //  Logic: NavBall Management
        // =========================================================

        public void SetNavBallMode(SpeedDisplayModes mode)
        {
            if (ActiveNavBallMode == mode && FlightGlobals.speedDisplayMode == mode) return;

            DebugLog($"Set NavBall Mode to: {mode}");
            ActiveNavBallMode = mode;
            FlightGlobals.SetSpeedMode(mode);
            UpdateNavBallTitle();
        }

        public void CycleNavBallMode()
        {
            if (Instance != null && !Instance.CachedNavBallSync)
            {
                DebugLog($"Cycling NavBall Mode.");
                switch (FlightGlobals.speedDisplayMode)
                {
                    case SpeedDisplayModes.Surface:
                        Instance.SetNavBallMode(SpeedDisplayModes.Orbit);
                        break;
                    case SpeedDisplayModes.Orbit:
                        if (FlightGlobals.fetch.VesselTarget != null)
                            Instance.SetNavBallMode(SpeedDisplayModes.Target);
                        else
                            Instance.SetNavBallMode(SpeedDisplayModes.Surface);
                        break;
                    case SpeedDisplayModes.Target:
                        Instance.SetNavBallMode(SpeedDisplayModes.Surface);
                        break;
                }
            }
        }

        private static void ApplyKspModeSync(SpeedModeEx mode)
        {
            SpeedDisplayModes targetMode = (mode == SpeedModeEx.Orbit) ? SpeedDisplayModes.Orbit :
                                           (mode == SpeedModeEx.Target) ? SpeedDisplayModes.Target :
                                            SpeedDisplayModes.Surface;

            if (Instance != null && ActiveNavBallMode != targetMode)
            {
                DebugLog("Syncing NavBall to Speed Mode.");
                Instance.SetNavBallMode(targetMode);
            }
        }

        private void CheckAndFixNavBall()
        {
            if (CachedNavBallSync)
            {
                ApplyKspModeSync(ActiveSpeedMode);
            }
            else
            {
                if (ActiveNavBallMode == SpeedDisplayModes.Target && FlightGlobals.fetch.VesselTarget == null)
                {
                    SetNavBallMode(ShouldBeInSurfaceMode(FlightGlobals.ActiveVessel) ? SpeedDisplayModes.Surface : SpeedDisplayModes.Orbit);
                }
                else
                {
                    if (FlightGlobals.speedDisplayMode != ActiveNavBallMode)
                    {
                        FlightGlobals.SetSpeedMode(ActiveNavBallMode);
                    }
                }
            }
            UpdateNavBallTitle();
        }

        // =========================================================
        //  Logic: Helper Calculations
        // =========================================================

        private bool IsSurfaceGroup(SpeedModeEx mode)
        {
            switch (mode)
            {
                case SpeedModeEx.Surface_TAS:
                case SpeedModeEx.Surface_IAS:
                case SpeedModeEx.Surface_EAS:
                case SpeedModeEx.Surface_Q:
                    return true;
                default:
                    return false;
            }
        }

        private bool ShouldBeInSurfaceMode(Vessel v)
        {
            if (v.LandedOrSplashed) return true;
            if (_settingsGeneral == null || _settingsGeneral.autoSpeedMode == AutoSpeedMode.Off) return true;

            CelestialBody body = v.mainBody;

            float refAlt;
            if (_settingsGeneral.autoSpeedMode == AutoSpeedMode.Stock)
                refAlt = (float)(body.Radius * 0.06);
            else
            {
                string currentBodyName = body.name;
                if (_cachedBodyName != currentBodyName || _cachedRefAlt < 0)
                {
                    _cachedBodyName = currentBodyName;

                    if (BodyThresholds.TryGetValue(currentBodyName, out float cfgAlt))
                        _cachedRefAlt = cfgAlt;
                    else if (body.atmosphere)
                        _cachedRefAlt = (float)(body.atmosphereDepth * 0.8);
                    else
                        _cachedRefAlt = (float)(body.Radius * 0.06);
                }
                refAlt = _cachedRefAlt;
            }

            float hysteresis = IsSurfaceGroup(ActiveSpeedMode) ? 1.0f : (5.5f / 6.0f);
            float mult = (float)_settingsGeneral.autoSwitchThreshold / 100.0f;

            return v.altitude < (refAlt * hysteresis * mult);
        }

        // =========================================================
        //  UI Helpers & Initialization
        // =========================================================

        private void RefreshSettingsCache()
        {
            _settingsGeneral = General;
            _settingsDisplay = Display;
            _settingsUnits = Units;

            if (_settingsGeneral != null)
            {
                CachedNavBallSync = _settingsGeneral.navBallSync;
                CachedNavBallAutoSwitch = _settingsGeneral.navBallAutoSwitch;
            }

            // Pre-calculate Unit Data for Rendering
            int enumCount = System.Enum.GetNames(typeof(SpeedUnit)).Length;
            if (_unitDataCache == null || _unitDataCache.Length != enumCount)
                _unitDataCache = new UnitRenderData[enumCount];

            void SetData(SpeedUnit u, double mult, string sym, int dig)
            {
                _unitDataCache[(int)u] = new UnitRenderData { multiplier = mult, symbol = sym, digits = dig };
            }

            if (_settingsUnits != null)
            {
                var s = _settingsUnits;
                SetData(SpeedUnit.Ms, 1.0, " m/s", s.digitsMs);
                SetData(SpeedUnit.Kmh, M_TO_KMH, " km/h", s.digitsKmh);
                SetData(SpeedUnit.Mph, M_TO_MPH, " mph", s.digitsMph);
                SetData(SpeedUnit.Knots, M_TO_KNOTS, " knots", s.digitsKnots);
                SetData(SpeedUnit.Fts, M_TO_FTS, " ft/s", s.digitsFts);
                SetData(SpeedUnit.Mach, 1.0, " Mach", s.digitsMach);
            }
            else
            {
                SetData(SpeedUnit.Ms, 1.0, " m/s", 1);
                SetData(SpeedUnit.Kmh, M_TO_KMH, " km/h", 1);
                SetData(SpeedUnit.Mph, M_TO_MPH, " mph", 1);
                SetData(SpeedUnit.Knots, M_TO_KNOTS, " knots", 1);
                SetData(SpeedUnit.Fts, M_TO_FTS, " ft/s", 1);
                SetData(SpeedUnit.Mach, 1.0, " Mach", 2);
            }
        }

        private void CopyTextStyle(TextMeshProUGUI source, TextMeshProUGUI dest, float sizeMultiplier)
        {
            dest.font = source.font;
            dest.fontSize = source.fontSize * sizeMultiplier;
            dest.color = source.color;
            dest.enableWordWrapping = false;
            dest.enableAutoSizing = false;
            dest.raycastTarget = false;

            RectTransform rtSource = source.rectTransform;
            RectTransform rtDest = dest.rectTransform;
            rtDest.anchorMin = rtSource.anchorMin;
            rtDest.anchorMax = rtSource.anchorMax;
            rtDest.pivot = rtSource.pivot;
            rtDest.anchoredPosition = rtSource.anchoredPosition;
            rtDest.sizeDelta = rtSource.sizeDelta;
        }

        public void UpdateSpeedTitle()
        {
            if (_speedModeText == null) return;

            switch (ActiveSpeedMode)
            {
                case SpeedModeEx.Surface_TAS: _speedModeText.text = Localizer.Format(FarUtils.IsFarLoaded ? "#NU_Title_TAS" : "#NU_Title_Surface"); break;
                case SpeedModeEx.Surface_IAS: _speedModeText.text = Localizer.Format("#NU_Title_IAS"); break;
                case SpeedModeEx.Surface_EAS: _speedModeText.text = Localizer.Format("#NU_Title_EAS"); break;
                case SpeedModeEx.Surface_Q: _speedModeText.text = Localizer.Format("#NU_Title_Q"); break;
                case SpeedModeEx.Vertical: _speedModeText.text = Localizer.Format("#NU_Title_Vert"); break;
                case SpeedModeEx.Target: _speedModeText.text = Localizer.Format("#NU_Title_Target"); break;
                case SpeedModeEx.Orbit: _speedModeText.text = Localizer.Format("#NU_Title_Orbit"); break;
            }
        }

        public void UpdateNavBallTitle()
        {
            if (_navBallModeText == null) return;

            switch (FlightGlobals.speedDisplayMode)
            {
                case SpeedDisplayModes.Surface: _navBallModeText.text = Localizer.Format("#NU_Nav_Surface"); break;
                case SpeedDisplayModes.Orbit: _navBallModeText.text = Localizer.Format("#NU_Nav_Orbit"); break;
                case SpeedDisplayModes.Target: _navBallModeText.text = Localizer.Format("#NU_Nav_Target"); break;
            }
        }

        // =========================================================
        //  Logging Helpers
        // =========================================================
        public static void SystemLog(string message)
        {
            Debug.Log($"[NavUnits] {message}");
        }

        public static void SystemErrorLog(string message)
        {
            Debug.LogError($"[NavUnits] [ERROR] {message}");
        }

        public static void DebugLog(string message)
        {
            if (General != null && General.debugMode)
            {
                Debug.Log($"[NavUnits] [DEBUG] {message}");
            }
        }

        public static void DebugErrorLog(string message)
        {
            if (General != null && General.debugMode)
            {
                Debug.LogError($"[NavUnits] [DEBUG] {message}");
            }
        }
    }

    // =========================================================================================
    //  Harmony Patches
    // =========================================================================================

    [HarmonyPatch(typeof(SpeedDisplay), "LateUpdate")]
    class Harmony_SpeedDisplay_Update
    {
        static bool Prefix(SpeedDisplay __instance)
        {
            // Block stock update logic
            return false;
        }
    }
}
