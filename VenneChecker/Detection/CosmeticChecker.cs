using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Reads equipped cosmetics from a VRRig and returns them as display strings.
    /// Shows what cosmetics a player is wearing (info only, not flagged as cheats).
    /// </summary>
    public static class CosmeticChecker
    {
        private static bool _loggedFields;

        /// <summary>
        /// Gets the list of cosmetics a player is currently wearing.
        /// Returns items like "Cosmetic: HatName", "Cosmetic: BadgeName".
        /// </summary>
        public static List<string> GetEquippedCosmetics(VRRig rig)
        {
            var cosmetics = new List<string>();
            if (rig == null) return cosmetics;

            Type rigType = rig.GetType();

            // Log cosmetic-related fields once for debugging
            if (!_loggedFields)
            {
                _loggedFields = true;
                LogCosmeticFields(rigType);
            }

            // Strategy 1: Read the cosmetic set (array of equipped items)
            try
            {
                ReadCosmeticSet(rig, rigType, cosmetics);
            }
            catch (Exception ex) { Log.Warn($"[COSMETIC] Set read failed: {ex.Message}"); }

            // Strategy 2: Read individual cosmetic slot fields
            if (cosmetics.Count == 0)
            {
                try { ReadCosmeticSlots(rig, rigType, cosmetics); }
                catch { }
            }

            // Strategy 3: Read concatStringOfCosmeticsAllowed as fallback info
            if (cosmetics.Count == 0)
            {
                try { ReadCosmeticString(rig, rigType, cosmetics); }
                catch { }
            }

            return cosmetics;
        }

        /// <summary>
        /// Reads the VRRig's cosmetic set — an array/list of CosmeticItem or similar.
        /// </summary>
        private static void ReadCosmeticSet(VRRig rig, Type rigType, List<string> cosmetics)
        {
            // Try common field names for the equipped cosmetic set
            string[] setFieldNames = {
                "cosmeticSet", "currentCosmeticSet", "mySet",
                "currentWornSet", "equippedCosmetics", "cosmetics"
            };

            foreach (string fieldName in setFieldNames)
            {
                FieldInfo f = rigType.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) continue;

                object setObj = f.GetValue(rig);
                if (setObj == null) continue;

                // If it has an "items" field/property (CosmeticSet.items)
                Type setType = setObj.GetType();

                // Try items array
                FieldInfo itemsField = setType.GetField("items",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                PropertyInfo itemsProp = setType.GetProperty("items",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                object itemsObj = null;
                if (itemsField != null) itemsObj = itemsField.GetValue(setObj);
                else if (itemsProp != null) itemsObj = itemsProp.GetValue(setObj, null);

                if (itemsObj is Array itemsArray)
                {
                    foreach (object item in itemsArray)
                    {
                        if (item == null) continue;
                        string name = GetCosmeticItemName(item);
                        if (!string.IsNullOrEmpty(name) && name != "null" && name != "NOTHING" && name != "EMPTY")
                            cosmetics.Add($"Cosmetic: {name}");
                    }
                    if (cosmetics.Count > 0) return;
                }

                // Try as IList
                if (itemsObj is System.Collections.IList itemsList)
                {
                    foreach (object item in itemsList)
                    {
                        if (item == null) continue;
                        string name = GetCosmeticItemName(item);
                        if (!string.IsNullOrEmpty(name) && name != "null" && name != "NOTHING" && name != "EMPTY")
                            cosmetics.Add($"Cosmetic: {name}");
                    }
                    if (cosmetics.Count > 0) return;
                }

                // Try reading the set object itself if it has displayName or itemName
                string setName = GetCosmeticItemName(setObj);
                if (!string.IsNullOrEmpty(setName))
                {
                    cosmetics.Add($"Cosmetic: {setName}");
                    return;
                }
            }
        }

        /// <summary>
        /// Tries reading individual cosmetic slot fields on VRRig.
        /// </summary>
        private static void ReadCosmeticSlots(VRRig rig, Type rigType, List<string> cosmetics)
        {
            // Look for fields that contain "cosmetic" and hold items
            foreach (FieldInfo f in rigType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string fname = f.Name.ToLower();
                if (!fname.Contains("cosmetic") && !fname.Contains("hat") &&
                    !fname.Contains("badge") && !fname.Contains("holdable") &&
                    !fname.Contains("face") && !fname.Contains("skin"))
                    continue;

                // Skip string fields (those are the "allowed" lists, not equipped items)
                if (f.FieldType == typeof(string)) continue;

                try
                {
                    object val = f.GetValue(rig);
                    if (val == null) continue;

                    string name = GetCosmeticItemName(val);
                    if (!string.IsNullOrEmpty(name) && name != "null" && name != "NOTHING")
                        cosmetics.Add($"Cosmetic: {name}");
                }
                catch { }
            }
        }

        /// <summary>
        /// Reads the concatStringOfCosmeticsAllowed and extracts item names from it.
        /// This is a comma-separated or concatenated string of owned cosmetics.
        /// </summary>
        private static void ReadCosmeticString(VRRig rig, Type rigType, List<string> cosmetics)
        {
            string[] fieldNames = {
                "concatStringOfCosmeticsAllowed", "cosmeticsAllowed", "cosmeticString"
            };

            foreach (string fieldName in fieldNames)
            {
                FieldInfo f = rigType.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) continue;

                string val = f.GetValue(rig)?.ToString();
                if (string.IsNullOrEmpty(val) || val.Length < 3) continue;

                // This is typically a long concatenated string. Just note it exists.
                // We can't easily split it without knowing the delimiter, so just report the count
                Log.Info($"[COSMETIC] {fieldName} length: {val.Length}");

                // Try splitting by common delimiters
                string[] parts = val.Split(new[] { ',', ';', '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    int shown = 0;
                    foreach (string part in parts)
                    {
                        string trimmed = part.Trim();
                        if (trimmed.Length > 2 && shown < 8) // Cap at 8 to not flood the UI
                        {
                            cosmetics.Add($"Cosmetic: {trimmed}");
                            shown++;
                        }
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Extracts a display name from a CosmeticItem-like object.
        /// </summary>
        private static string GetCosmeticItemName(object item)
        {
            if (item == null) return null;
            Type t = item.GetType();

            // Try common name properties/fields
            string[] nameFields = { "displayName", "itemName", "name", "Name", "cosmeticName", "overrideDisplayName" };
            foreach (string n in nameFields)
            {
                try
                {
                    PropertyInfo p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        string val = p.GetValue(item, null)?.ToString();
                        if (!string.IsNullOrEmpty(val) && val != "NOTHING" && val != "null")
                            return val;
                    }
                }
                catch { }

                try
                {
                    FieldInfo f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        string val = f.GetValue(item)?.ToString();
                        if (!string.IsNullOrEmpty(val) && val != "NOTHING" && val != "null")
                            return val;
                    }
                }
                catch { }
            }

            // Last resort: ToString if it looks meaningful
            string str = item.ToString();
            if (str != null && !str.Contains("UnityEngine") && !str.Contains("System.") && str.Length < 40)
                return str;

            return null;
        }

        /// <summary>
        /// Logs all cosmetic-related fields on VRRig for debugging (once).
        /// </summary>
        private static void LogCosmeticFields(Type rigType)
        {
            Log.Info("[COSMETIC] VRRig cosmetic-related fields:");
            foreach (FieldInfo f in rigType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string fname = f.Name.ToLower();
                if (fname.Contains("cosmetic") || fname.Contains("hat") || fname.Contains("badge") ||
                    fname.Contains("holdable") || fname.Contains("face") || fname.Contains("skin") ||
                    fname.Contains("set") || fname.Contains("worn"))
                {
                    Log.Info($"  Field: {f.Name} ({f.FieldType.Name})");
                }
            }
        }
    }
}
