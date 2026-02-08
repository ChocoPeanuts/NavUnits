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
            {
                NavUnits.Instance.CycleUnit();
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                NavUnits.Instance.CycleSpeedMode();
            }
        }
    }

    public class NavBallClickDetector : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                NavUnits.Instance.CycleNavBallMode();
            }
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
            Debug.Log($"[NavUnits] Loading Body Configs... Found {nodes.Length} config nodes.");

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

        // ---------------------------------------------------------
        // State Variables
        // ---------------------------------------------------------
        public static SpeedUnit ActiveUnit = SpeedUnit.Ms;
        public static SpeedModeEx ActiveSpeedMode = SpeedModeEx.Surface_TAS;

        private ITargetable _previousTarget;
        private SpeedDisplayModes _managedNavBallMode;

        private bool _wasSurfaceCondition = true;
        private bool _navBallWasSurfaceCondition = true;

        // ---------------------------------------------------------
        // Cache Variables (Optimization)
        // ---------------------------------------------------------
        private SpeedDisplay _cachedSpeedDisplay;
        private TextMeshProUGUI _speedModeText;    // Left text (Speed Mode)
        private TextMeshProUGUI _navBallModeText;  // Right text (NavBall Mode)

        private string _cachedBodyName;
        private float _cachedRefAlt = -1f;

        public bool CachedNavBallSync { get; private set; } = true;
        public bool CachedNavBallAutoSwitch { get; private set; } = true;

        private NU_GeneralSettings _settingsGeneral;
        private NU_DisplaySettings _settingsDisplay;
        private NU_UnitSettings _settingsUnits;

        // ---------------------------------------------------------
        // Static Data & Constants
        // ---------------------------------------------------------
        public static readonly List<SpeedUnit> UnitOrder = new List<SpeedUnit> { SpeedUnit.Ms, SpeedUnit.Kmh, SpeedUnit.Mph, SpeedUnit.Knots, SpeedUnit.Fts, SpeedUnit.Mach };
        public static Dictionary<string, float> BodyThresholds = new Dictionary<string, float>();

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

        // ---------------------------------------------------------
        // Settings Accessors
        // ---------------------------------------------------------
        public static NU_GeneralSettings General => HighLogic.CurrentGame.Parameters.CustomParams<NU_GeneralSettings>();
        public static NU_DisplaySettings Display => HighLogic.CurrentGame.Parameters.CustomParams<NU_DisplaySettings>();
        public static NU_UnitSettings Units => HighLogic.CurrentGame.Parameters.CustomParams<NU_UnitSettings>();


        // =========================================================
        //  Public API / Static Helpers
        // =========================================================

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

        public static int GetUnitDigits(SpeedUnit unit)
        {
            if (Instance == null || Instance._settingsUnits == null) return 1;
            var s = Instance._settingsUnits;
            switch (unit)
            {
                case SpeedUnit.Ms: return s.digitsMs;
                case SpeedUnit.Kmh: return s.digitsKmh;
                case SpeedUnit.Mph: return s.digitsMph;
                case SpeedUnit.Knots: return s.digitsKnots;
                case SpeedUnit.Fts: return s.digitsFts;
                case SpeedUnit.Mach: return s.digitsMach;
                default: return 1;
            }
        }

        // =========================================================
        //  Lifecycle Methods
        // =========================================================

        public void Start()
        {
            Instance = this;

            // Initialize Harmony
            var harmony = new Harmony("com.ChocoPeanuts.NavUnits");
            harmony.PatchAll();

            GameEvents.OnGameSettingsApplied.Add(OnGameSettingsApplied);

            // Initial Settings Load
            RefreshSettingsCache();
            ActiveUnit = GetPreferredUnit();

            _previousTarget = FlightGlobals.fetch.VesselTarget;
            _managedNavBallMode = FlightGlobals.speedDisplayMode;

            // --- Display & UI Setup ---
            _cachedSpeedDisplay = FindObjectOfType<SpeedDisplay>();
            if (_cachedSpeedDisplay != null)
            {
                // 1. Remove Stock Speed Mode Button logic
                var btn = _cachedSpeedDisplay.GetComponentInChildren<Button>() ?? _cachedSpeedDisplay.textSpeed.GetComponentInParent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.enabled = false;
                }

                // 2. Setup Custom Titles (Speed & NavBall Mode)
                if (_cachedSpeedDisplay.textTitle is TextMeshProUGUI stockTitle)
                {
                    stockTitle.enabled = false;
                    stockTitle.raycastTarget = false;

                    // Left: Speed Mode Title
                    GameObject leftObj = new GameObject("NU_SpeedModeTitle");
                    leftObj.transform.SetParent(stockTitle.transform.parent, false);
                    _speedModeText = leftObj.AddComponent<TextMeshProUGUI>();
                    CopyTextStyle(stockTitle, _speedModeText, 0.8f);
                    _speedModeText.rectTransform.anchoredPosition += new Vector2(-13f, 0f);
                    _speedModeText.alignment = TextAlignmentOptions.Left;
                    _speedModeText.text = "";

                    // Right: NavBall Mode Title
                    GameObject rightObj = new GameObject("NU_NavBallModeTitle");
                    rightObj.transform.SetParent(stockTitle.transform.parent, false);
                    _navBallModeText = rightObj.AddComponent<TextMeshProUGUI>();
                    CopyTextStyle(stockTitle, _navBallModeText, 0.65f);
                    _navBallModeText.rectTransform.anchoredPosition += new Vector2(17f, 0f);
                    _navBallModeText.alignment = TextAlignmentOptions.Right;
                    _navBallModeText.text = "";
                }

                // 3. Setup Speed Display Text
                if (_cachedSpeedDisplay.textSpeed is TextMeshProUGUI tmSpeed)
                {
                    tmSpeed.enableWordWrapping = false;
                    tmSpeed.fontSize *= 1.0f;
                    tmSpeed.raycastTarget = false;
                }

                // 4. Attach Click Detectors
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

                // 5. Create NavBall Hit Area
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

            // Disable FAR GUI to avoid overlap
            FarUtils.SetFARDisplay(false);

            // Initial State Check
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel != null)
            {
                bool isSurfaceCondition = ShouldBeInSurfaceMode(vessel);
                _wasSurfaceCondition = isSurfaceCondition;
                _navBallWasSurfaceCondition = isSurfaceCondition;

                if (FlightGlobals.fetch.VesselTarget != null) SetSpeedMode(SpeedModeEx.Target);
                else SetSpeedMode(isSurfaceCondition ? SpeedModeEx.Surface_TAS : SpeedModeEx.Orbit);
            }
            CheckAndFixMode();
            CheckAndFixUnit();

            UpdateSpeedTitle();
            UpdateNavBallTitle();
        }

        public void OnDestroy()
        {
            FarUtils.SetFARDisplay(true); // Restore FAR GUI
            GameEvents.OnGameSettingsApplied.Remove(OnGameSettingsApplied);

            if (_cachedSpeedDisplay != null && _cachedSpeedDisplay.textTitle != null)
            {
                _cachedSpeedDisplay.textTitle.enabled = true;
            }
        }

        // =========================================================
        //  Settings Management
        // =========================================================

        public void OnGameSettingsApplied()
        {
            Debug.Log("[NavUnits] Game Settings Applied! Refreshing state...");
            RefreshSettingsCache();
            CheckAndFixMode();
            CheckAndFixUnit();
        }

        private void RefreshSettingsCache()
        {
            // Cache settings objects to avoid HighLogic lookups every frame
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
                // Fallback / Defaults
                SetData(SpeedUnit.Ms, 1.0, " m/s", 1);
                SetData(SpeedUnit.Kmh, M_TO_KMH, " km/h", 1);
                SetData(SpeedUnit.Mph, M_TO_MPH, " mph", 1);
                SetData(SpeedUnit.Knots, M_TO_KNOTS, " knots", 1);
                SetData(SpeedUnit.Fts, M_TO_FTS, " ft/s", 1);
                SetData(SpeedUnit.Mach, 1.0, " Mach", 2);
            }
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
                    // Speed Display Switch
                    bool isSpeedoAuto = (_settingsGeneral != null && _settingsGeneral.autoSpeedMode != AutoSpeedMode.Off);
                    if (isSpeedoAuto) SetSpeedMode(SpeedModeEx.Target);

                    // NavBall Switch (Sync OFF only)
                    if (!CachedNavBallSync && CachedNavBallAutoSwitch)
                    {
                        SetNavBallMode(SpeedDisplayModes.Target);
                    }
                }
                // 2. Target Lost
                else if (_previousTarget != null && currentTarget == null)
                {
                    // Speed Display Revert
                    if (ActiveSpeedMode == SpeedModeEx.Target)
                    {
                        SetSpeedMode(isSurfaceCondition ? SpeedModeEx.Surface_TAS : SpeedModeEx.Orbit);
                    }

                    // NavBall Revert
                    if (_managedNavBallMode == SpeedDisplayModes.Target)
                    {
                        SetNavBallMode(isSurfaceCondition ? SpeedDisplayModes.Surface : SpeedDisplayModes.Orbit);
                    }
                }
                _previousTarget = currentTarget;
            }

            // ---------------------------------------------------------------------
            //  B. Speed Display Auto-Switching (Altitude based)
            // ---------------------------------------------------------------------
            if (_settingsGeneral != null && _settingsGeneral.autoSpeedMode != AutoSpeedMode.Off)
            {
                if (_wasSurfaceCondition && !isSurfaceCondition)
                {
                    if (IsSurfaceGroup(ActiveSpeedMode)) SetSpeedMode(SpeedModeEx.Orbit);
                }
                else if (!_wasSurfaceCondition && isSurfaceCondition)
                {
                    if (ActiveSpeedMode == SpeedModeEx.Orbit) SetSpeedMode(SpeedModeEx.Surface_TAS);
                }
                _wasSurfaceCondition = isSurfaceCondition;
            }

            // ---------------------------------------------------------------------
            //  C. NavBall Synchronization & Logic
            // ---------------------------------------------------------------------

            // Case 1: Sync Enabled (Follow Speed Display)
            if (CachedNavBallSync)
            {
                ApplyKspModeSync(ActiveSpeedMode);
                _managedNavBallMode = FlightGlobals.speedDisplayMode;
            }

            // Case 2: Sync Disabled (Independent Logic)
            if (!CachedNavBallSync)
            {
                if (CachedNavBallAutoSwitch && _managedNavBallMode != SpeedDisplayModes.Target)
                {
                    if (_navBallWasSurfaceCondition && !isSurfaceCondition)
                    {
                        if (_managedNavBallMode == SpeedDisplayModes.Surface) SetNavBallMode(SpeedDisplayModes.Orbit);
                    }
                    else if (!_navBallWasSurfaceCondition && isSurfaceCondition)
                    {
                        if (_managedNavBallMode == SpeedDisplayModes.Orbit) SetNavBallMode(SpeedDisplayModes.Surface);
                    }
                    _navBallWasSurfaceCondition = isSurfaceCondition;
                }

                // Force apply managed state to prevent KSP overriding (Anti-Flicker)
                if (FlightGlobals.speedDisplayMode != _managedNavBallMode)
                {
                    SetNavBallMode(_managedNavBallMode);
                }
            }

            // ---------------------------------------------------------------------
            //  D. Rendering (GC Optimized)
            // ---------------------------------------------------------------------

            if (_cachedSpeedDisplay == null || _speedModeText == null) return;

            _sb.Length = 0; // Clear Builder

            double rawSpeed = 0;
            bool isQ = false;

            // 1. Fetch Raw Speed
            switch (ActiveSpeedMode)
            {
                case SpeedModeEx.Surface_TAS:
                    rawSpeed = vessel.srfSpeed;
                    break;
                case SpeedModeEx.Surface_IAS:
                    rawSpeed = FarUtils.GetIAS();
                    break;
                case SpeedModeEx.Surface_EAS:
                    rawSpeed = FarUtils.GetEAS();
                    break;
                case SpeedModeEx.Surface_Q:
                    rawSpeed = FarUtils.GetQ(); isQ = true;
                    break;
                case SpeedModeEx.Vertical:
                    rawSpeed = vessel.verticalSpeed;
                    break;
                case SpeedModeEx.Orbit:
                    rawSpeed = FlightGlobals.ship_obtSpeed;
                    break;
                case SpeedModeEx.Target:
                    rawSpeed = FlightGlobals.ship_tgtSpeed;
                    if (currentTarget != null)
                    {
                        Vector3d shipVel = vessel.obt_velocity;
                        Vector3d tgtVel = currentTarget.GetObtVelocity();
                        Vector3d toTarget = currentTarget.GetTransform().position - vessel.GetTransform().position;

                        if (Vector3d.Dot(shipVel - tgtVel, toTarget) > 0) rawSpeed *= -1;
                    }
                    break;
            }

            // 2. Validate Mach Unit (Prevent Mach in Orbit/Vacuum)
            if (ActiveUnit == SpeedUnit.Mach && (ActiveSpeedMode == SpeedModeEx.Orbit || ActiveSpeedMode == SpeedModeEx.Target || ActiveSpeedMode == SpeedModeEx.Vertical))
                ActiveUnit = GetPreferredUnit();

            // 3. Prepare Display Data
            double displayValue;
            int digits;
            string symbol;

            if (isQ)
            {
                displayValue = rawSpeed;
                digits = (_settingsDisplay != null) ? _settingsDisplay.digitsQ : 1;
                symbol = " kPa";
            }
            else
            {
                UnitRenderData currentData = _unitDataCache[(int)ActiveUnit];

                displayValue = (ActiveUnit == SpeedUnit.Mach) ? vessel.mach : rawSpeed * currentData.multiplier;

                digits = currentData.digits;
                symbol = currentData.symbol;
            }

            // 4. Final Formatting
            digits = Mathf.Clamp(digits, 0, FloatFormats.Length - 1);
            double factor = Pow10[digits];
            displayValue = System.Math.Truncate(displayValue * factor) / factor;

            if (displayValue >= 0 && (ActiveSpeedMode == SpeedModeEx.Vertical || ActiveSpeedMode == SpeedModeEx.Target)) _sb.Append('+');

            _sb.Append(displayValue.ToString(FloatFormats[digits], CultureInfo.InvariantCulture));
            _sb.Append(symbol);

            if (_cachedSpeedDisplay.textSpeed != null)
                _cachedSpeedDisplay.textSpeed.SetText(_sb);
        }

        // =========================================================
        //  Unit Logic (Selection & Validation)
        // =========================================================

        public static SpeedUnit GetPreferredUnit()
        {
            if (Instance == null || Instance._settingsUnits == null) return SpeedUnit.Ms;
            int idx = Instance._settingsUnits.defaultUnitIndex;

            if (idx >= 0 && idx < UnitOrder.Count && IsValidUnitForCurrentMode(UnitOrder[idx]))
                return UnitOrder[idx];

            if (idx >= 0 && idx < UnitOrder.Count)
            {
                return UnitOrder[idx];
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
            {
                SpeedUnit oldUnit = ActiveUnit;
                SpeedUnit newUnit = GetPreferredUnit();
                if (oldUnit != newUnit) ActiveUnit = newUnit;
            }
        }

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
                    return;
                }
            }
            ActiveUnit = GetPreferredUnit();
        }

        // =========================================================
        //  Speed Mode Logic
        // =========================================================

        public static void SetSpeedMode(SpeedModeEx newMode)
        {
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
            switch (ActiveSpeedMode)
            {
                case SpeedModeEx.Surface_TAS:
                    if (TryGoToFarMode()) return;
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

        private bool TryGoToFarMode()
        {
            if (_settingsDisplay == null) return false;
            if (!FarUtils.IsFarLoaded || FlightGlobals.ActiveVessel.staticPressurekPa <= 0) return false;
            if (_settingsDisplay.enableIAS) { SetSpeedMode(SpeedModeEx.Surface_IAS); return true; }
            if (_settingsDisplay.enableEAS) { SetSpeedMode(SpeedModeEx.Surface_EAS); return true; }
            if (_settingsDisplay.enableQ) { SetSpeedMode(SpeedModeEx.Surface_Q); return true; }
            return false;
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
                var v = FlightGlobals.ActiveVessel;
                if (v != null && !ShouldBeInSurfaceMode(v))
                {
                    SetSpeedMode(SpeedModeEx.Orbit);
                }
                else
                {
                    SetSpeedMode(SpeedModeEx.Surface_TAS);
                }
            }
        }

        // =========================================================
        //  NavBall Logic
        // =========================================================

        public void CycleNavBallMode()
        {
            if (Instance != null && !Instance.CachedNavBallSync)
            {
                SpeedDisplayModes current = FlightGlobals.speedDisplayMode;
                SpeedDisplayModes next = SpeedDisplayModes.Surface;

                switch (current)
                {
                    case SpeedDisplayModes.Surface: next = SpeedDisplayModes.Orbit; break;
                    case SpeedDisplayModes.Orbit: next = SpeedDisplayModes.Target; break;
                    case SpeedDisplayModes.Target: next = SpeedDisplayModes.Surface; break;
                }

                if (next == SpeedDisplayModes.Target && FlightGlobals.fetch.VesselTarget == null)
                {
                    next = SpeedDisplayModes.Surface;
                }

                Instance.SetNavBallMode(next);
            }
        }

        public void SetNavBallMode(SpeedDisplayModes mode)
        {
            _managedNavBallMode = mode;
            FlightGlobals.SetSpeedMode(mode);
            UpdateNavBallTitle();
        }

        private static void ApplyKspModeSync(SpeedModeEx mode)
        {
            SpeedDisplayModes targetMode = (mode == SpeedModeEx.Orbit) ? SpeedDisplayModes.Orbit :
                                           (mode == SpeedModeEx.Target) ? SpeedDisplayModes.Target :
                                            SpeedDisplayModes.Surface;

            if (FlightGlobals.speedDisplayMode != targetMode)
            {
                FlightGlobals.SetSpeedMode(targetMode);
                if (Instance != null) Instance.UpdateNavBallTitle();
            }
        }

        // =========================================================
        //  UI / Visual Updates
        // =========================================================

        public void UpdateSpeedTitle()
        {
            if (_speedModeText == null) return;

            string titleKey = "";
            switch (ActiveSpeedMode)
            {
                case SpeedModeEx.Surface_TAS: titleKey = FarUtils.IsFarLoaded ? "#NU_Title_TAS" : "#NU_Title_Surface"; break;
                case SpeedModeEx.Surface_IAS: titleKey = "#NU_Title_IAS"; break;
                case SpeedModeEx.Surface_EAS: titleKey = "#NU_Title_EAS"; break;
                case SpeedModeEx.Surface_Q: titleKey = "#NU_Title_Q"; break;
                case SpeedModeEx.Vertical: titleKey = "#NU_Title_Vert"; break;
                case SpeedModeEx.Target: titleKey = "#NU_Title_Target"; break;
                case SpeedModeEx.Orbit: titleKey = "#NU_Title_Orbit"; break;
            }
            _speedModeText.text = Localizer.Format(titleKey);
        }

        public void UpdateNavBallTitle()
        {
            if (_navBallModeText == null) return;

            string navTextKey = "";
            switch (FlightGlobals.speedDisplayMode)
            {
                case SpeedDisplayModes.Surface: navTextKey = "#NU_Nav_Surface"; break;
                case SpeedDisplayModes.Orbit: navTextKey = "#NU_Nav_Orbit"; break;
                case SpeedDisplayModes.Target: navTextKey = "#NU_Nav_Target"; break;
            }
            _navBallModeText.text = Localizer.Format(navTextKey);
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

        // =========================================================
        //  Math & Calculation Helpers
        // =========================================================

        private bool IsSurfaceGroup(SpeedModeEx mode)
        {
            //return mode == SpeedModeEx.Surface_TAS || mode == SpeedModeEx.Surface_IAS || mode == SpeedModeEx.Surface_EAS || mode == SpeedModeEx.Surface_Q;
            return (int)mode <= 3;
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
