using System;
using System.Reflection;
using UnityEngine;

namespace NavUnits
{
    /// Handles interaction with Ferram Aerospace Research (FAR) via Reflection.
    /// This ensures a "soft dependency", allowing the mod to run even if FAR is not installed.
    public static class FarUtils
    {
        private static bool _hasChecked = false;
        private static bool _isFarAvailable = false;

        // =========================================================
        //  Internal Delegates (Cached Reflection)
        // =========================================================
        private static Func<double> _fetchIas;
        private static Func<double> _fetchEas;
        private static Func<double> _fetchQ; // Dynamic Pressure (kPa)
        private static Func<bool?, Vessel, bool> _toggleFarGui;

        /// Checks if the FAR assembly is loaded and binds the necessary API methods.
        /// Returns cached result on subsequent calls.
        public static bool IsFarLoaded
        {
            get
            {
                if (_hasChecked) return _isFarAvailable;
                _hasChecked = true;
                _isFarAvailable = false;

                foreach (var assembly in AssemblyLoader.loadedAssemblies)
                {
                    if (assembly.name != "FerramAerospaceResearch") continue;

                    Type farApi = assembly.assembly.GetType("FerramAerospaceResearch.FARAPI");
                    if (farApi == null) break;

                    // Helper to bind methods quickly
                    T Bind<T>(string methodName) where T : class
                    {
                        MethodInfo info = farApi.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                        return (info != null) ? Delegate.CreateDelegate(typeof(T), info) as T : null;
                    }

                    // Bind API methods
                    _fetchIas = Bind<Func<double>>("ActiveVesselIAS");
                    _fetchEas = Bind<Func<double>>("ActiveVesselEAS");
                    _fetchQ = Bind<Func<double>>("ActiveVesselDynPres");

                    // ToggleAirspeedDisplay signature: (bool? show, Vessel v) -> bool
                    MethodInfo toggleInfo = farApi.GetMethod("ToggleAirspeedDisplay", BindingFlags.Public | BindingFlags.Static);
                    if (toggleInfo != null)
                        _toggleFarGui = (Func<bool?, Vessel, bool>)Delegate.CreateDelegate(typeof(Func<bool?, Vessel, bool>), toggleInfo);

                    // If basic methods are found, consider FAR loaded
                    if (_fetchIas != null)
                    {
                        _isFarAvailable = true;
                        NavUnits.SystemLog("FAR detected. API linked successfully.");
                    }
                    break;
                }
                return _isFarAvailable;
            }
        }

        // =========================================================
        //  Public API Wrappers
        // =========================================================

        public static double GetIAS() => (IsFarLoaded && _fetchIas != null) ? _fetchIas() : 0d;

        public static double GetEAS() => (IsFarLoaded && _fetchEas != null) ? _fetchEas() : 0d;

        /// Gets Dynamic Pressure (Q) in kPa.
        public static double GetQ() => (IsFarLoaded && _fetchQ != null) ? _fetchQ() : 0d;

        /// Toggles the visibility of FAR's own flight GUI.
        public static void SetFARDisplay(bool enabled)
        {
            if (IsFarLoaded && _toggleFarGui != null)
            {
                try
                {
                    _toggleFarGui(enabled, null);
                }
                catch (Exception e)
                {
                    NavUnits.SystemErrorLog($"Error toggling FAR display: {e.Message}");
                }
            }
        }
    }
}
