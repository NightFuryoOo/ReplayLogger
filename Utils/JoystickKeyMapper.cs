using System.Collections.Generic;
using UnityEngine;

namespace ReplayLogger
{
    internal static class JoystickKeyMapper
    {
        private static readonly Dictionary<KeyCode, string> FriendlyNames = new()
        {
            { KeyCode.JoystickButton0,  "JoystickButton0 (A/Cross)" },
            { KeyCode.JoystickButton1,  "JoystickButton1 (B/Circle)" },
            { KeyCode.JoystickButton2,  "JoystickButton2 (X/Square)" },
            { KeyCode.JoystickButton3,  "JoystickButton3 (Y/Triangle)" },
            { KeyCode.JoystickButton4,  "JoystickButton4 (LB/L1)" },
            { KeyCode.JoystickButton5,  "JoystickButton5 (RB/R1)" },
            { KeyCode.JoystickButton6,  "JoystickButton6 (View/Select)" },
            { KeyCode.JoystickButton7,  "JoystickButton7 (Menu/Start)" },
            { KeyCode.JoystickButton8,  "JoystickButton8 (LS/ThumbL)" },
            { KeyCode.JoystickButton9,  "JoystickButton9 (RS/ThumbR)" },
            { KeyCode.JoystickButton10, "JoystickButton10" },
            { KeyCode.JoystickButton11, "JoystickButton11" },
            { KeyCode.JoystickButton12, "JoystickButton12" },
            { KeyCode.JoystickButton13, "JoystickButton13" },
            { KeyCode.JoystickButton14, "JoystickButton14" },
            { KeyCode.JoystickButton15, "JoystickButton15" },
            { KeyCode.JoystickButton16, "JoystickButton16" },
            { KeyCode.JoystickButton17, "JoystickButton17" },
            { KeyCode.JoystickButton18, "JoystickButton18" },
            { KeyCode.JoystickButton19, "JoystickButton19" }
        };

        internal static string FormatKey(KeyCode keyCode)
        {
            if (FriendlyNames.TryGetValue(keyCode, out string label))
            {
                return label;
            }

            return keyCode.ToString();
        }
    }
}
