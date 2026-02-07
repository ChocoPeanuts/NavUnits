# NavUnits

![KSP Version](https://img.shields.io/badge/KSP-1.12.x-orange.svg)
![License](https://img.shields.io/badge/License-MIT-blue.svg)
![Dependency](https://img.shields.io/badge/Dependency-Harmony-red.svg)

Customize speed units and display modes in Kerbal Space Program.  
Supports independent NavBall control and FAR integration.

---

## 🚀 Features

* **Custom Units:** `m/s`, `km/h`, `mph`, `knots`, `ft/s`, `Mach`
* **Advanced Modes:** Surface (TAS), Orbit, Target, and **Vertical Speed**
* **Independent NavBall Control:** Switch NavBall modes (Surface / Orbit / Target) independently from the speed display
* **FAR Support:** Adds IAS, EAS, and Dynamic Pressure (Q) modes when [**Ferram Aerospace Research**](https://github.com/ferram4/Ferram-Aerospace-Research) is installed
* **Smart Auto-Switch:** Automatically switches between Surface and Orbit modes based on altitude

---

## 🕹️ How to Use

Interact directly with the speed display area.

| Action | Target | Description |
| :--- | :--- | :--- |
| **Right Click** | Speed Value | Cycle **Speed Units** |
| **Left Click** | Speed Value | Cycle **Speed Modes** |
| **Left Click** | NavBall | Cycle **NavBall Modes** |

> [!IMPORTANT]
> Independent NavBall control requires **"NavBall Sync"** to be **disabled** in the settings menu.

---

## 🛠️ Settings

Access via: `Pause Menu` → `Settings` → `Unit Replacer`

### 1. General
* Auto-switch altitude thresholds
* NavBall synchronization and independent control options

### 2. Display
* Enable / Disable display modes (IAS, EAS, Vertical Speed, etc.)
* Set decimal precision for Dynamic Pressure (Q)

### 3. Units
* Enable / Disable individual units
* Set default units and decimal precision

---

## 📦 Installation

1. Download and extract the latest release.
2. Place the `NavUnits` folder into your `<KSP Root>/GameData/` directory.
3. **Bundled Dependency:** [**Harmony**](https://github.com/KSPModdingLibs/HarmonyKSP) (already included)

---

## ⚙️ Customizing Auto-Switch Altitudes

You can customize the altitude at which the speed mode automatically switches between **Surface** and **Orbit** for each celestial body.

### File Location
`NavUnits/Config/BodyConfig.cfg`

### How to Edit
Open the `.cfg` file with a text editor and change the `altitude` value (in meters).

```cfg
NAVUNITS_BODY_CONFIG
{
    BODY
    {
        name = Mun
        altitude = 8000
    }
}
```

> [!TIP]
> **If the config file or specific body is missing, the following default logic is applied:**  
>
> **Atmospheric Bodies
> - 80% of the atmosphere depth.
>
> **Non-Atmospheric Bodies
> - 6% of the body's radius (Stock behavior).  

---

## 📄 License

This mod is released under the **MIT License**.  
