using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Per-player behavioral anomaly detection that runs during a scan.
    /// Checks: tag distance, color validation, arm length/scale, PhotonView ownership, name mismatch.
    /// All checks use reflection to read VRRig fields — no compile-time dependency on private members.
    /// </summary>
    public static class BehaviorDetector
    {
        // ═══════════════════════════════════════════
        //  THRESHOLDS
        // ═══════════════════════════════════════════

        // Tag distance: GT enforces ~1.5m max reach. Cheats bypass IsPositionInRange.
        private const float MaxTagDistance = 3.5f;            // meters — generous to avoid false positives
        private const float MaxHandToHeadDistance = 4.0f;     // hand shouldn't be 4m from head

        // Color: GT restricts to 0-1 per channel, but some cheats set negative or >1 values
        private const float ColorMin = -0.01f;               // tiny tolerance for float imprecision
        private const float ColorMax = 1.01f;

        // Scale: GT gorilla scale is normally 1.0. Cheats set tiny or huge values.
        private const float MinScale = 0.3f;
        private const float MaxScale = 2.0f;

        // Arm length: normal is ~1.0. Long arms cheat sets much higher.
        private const float MaxArmLength = 1.6f;
        private const float MinArmLength = 0.4f;

        // ═══════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════

        /// <summary>
        /// Runs all behavioral checks on a VRRig and returns a list of flag strings.
        /// These go into PlayerInfo.BehaviorFlags on the ModCheckerPage.
        /// </summary>
        public static List<string> Analyze(VRRig rig)
        {
            var flags = new List<string>();
            if (rig == null) return flags;

            Type rigType = rig.GetType();

            try { CheckTagDistance(rig, rigType, flags); } catch (Exception ex) { Log.Warn($"[BEHAVIOR] Tag distance check failed: {ex.Message}"); }
            try { CheckColor(rig, rigType, flags); } catch (Exception ex) { Log.Warn($"[BEHAVIOR] Color check failed: {ex.Message}"); }
            try { CheckScale(rig, rigType, flags); } catch (Exception ex) { Log.Warn($"[BEHAVIOR] Scale check failed: {ex.Message}"); }
            try { CheckArmLength(rig, rigType, flags); } catch (Exception ex) { Log.Warn($"[BEHAVIOR] Arm length check failed: {ex.Message}"); }
            try { CheckPhotonViewOwnership(rig, flags); } catch (Exception ex) { Log.Warn($"[BEHAVIOR] PhotonView check failed: {ex.Message}"); }
            try { CheckNameMismatch(rig, rigType, flags); } catch (Exception ex) { Log.Warn($"[BEHAVIOR] Name mismatch check failed: {ex.Message}"); }

            if (flags.Count > 0)
                Log.Info($"[BEHAVIOR] {flags.Count} flags on rig {rig.gameObject.name}: {string.Join(", ", flags)}");

            return flags;
        }

        // ═══════════════════════════════════════════
        //  1. TAG DISTANCE VALIDATION
        //  Checks if hand transforms are impossibly far from head/body.
        //  Cheats that bypass IsPositionInRange can tag from across the map.
        // ═══════════════════════════════════════════

        private static void CheckTagDistance(VRRig rig, Type rigType, List<string> flags)
        {
            Vector3 headPos = rig.transform.position; // VRRig root is roughly at body

            // Try to get head transform position for more accuracy
            Transform headTransform = TryGetTransform(rig, rigType, "headConstraint", "head", "headBone");
            if (headTransform != null)
                headPos = headTransform.position;

            // Check left hand
            Transform leftHand = TryGetTransform(rig, rigType, "leftHandTransform", "leftHand", "leftHandConstraint");
            if (leftHand != null)
            {
                float dist = Vector3.Distance(leftHand.position, headPos);
                if (dist > MaxHandToHeadDistance)
                {
                    flags.Add($"Long Arm Left ({dist:F1}m)");
                    Log.Info($"[BEHAVIOR] Left hand {dist:F1}m from head (max {MaxHandToHeadDistance}m)");
                }
            }

            // Check right hand
            Transform rightHand = TryGetTransform(rig, rigType, "rightHandTransform", "rightHand", "rightHandConstraint");
            if (rightHand != null)
            {
                float dist = Vector3.Distance(rightHand.position, headPos);
                if (dist > MaxHandToHeadDistance)
                {
                    flags.Add($"Long Arm Right ({dist:F1}m)");
                    Log.Info($"[BEHAVIOR] Right hand {dist:F1}m from head (max {MaxHandToHeadDistance}m)");
                }
            }
        }

        // ═══════════════════════════════════════════
        //  2. COLOR VALIDATION
        //  GT restricts color to 0-1 per channel. Cheats can set out-of-range
        //  values or cycle colors rapidly (rainbow/seizure effects).
        // ═══════════════════════════════════════════

        private static void CheckColor(VRRig rig, Type rigType, List<string> flags)
        {
            // VRRig stores color as separate R/G/B float fields or as a Color
            // Common field names: playerColor, materialsToChangeTo, colorR/colorG/colorB

            // Try reading color fields
            Color? color = null;

            // Strategy 1: Read individual R/G/B fields
            float? r = ReadFloat(rig, rigType, "red");
            float? g = ReadFloat(rig, rigType, "green");
            float? b = ReadFloat(rig, rigType, "blue");

            if (r.HasValue && g.HasValue && b.HasValue)
            {
                color = new Color(r.Value, g.Value, b.Value);
            }

            // Strategy 2: Read playerColor or matColor
            if (!color.HasValue)
            {
                object colorObj = ReadField(rig, rigType, "playerColor", "matColor", "bodyColor");
                if (colorObj is Color c)
                    color = c;
            }

            // Strategy 3: Read materialsToChangeTo array — first material's color
            if (!color.HasValue)
            {
                object matsObj = ReadField(rig, rigType, "materialsToChangeTo");
                if (matsObj is Material[] mats && mats.Length > 0 && mats[0] != null)
                {
                    color = mats[0].color;
                }
            }

            if (!color.HasValue) return;

            Color col = color.Value;

            // Check out-of-range values
            if (col.r < ColorMin || col.r > ColorMax ||
                col.g < ColorMin || col.g > ColorMax ||
                col.b < ColorMin || col.b > ColorMax)
            {
                flags.Add($"Invalid Color ({col.r:F2},{col.g:F2},{col.b:F2})");
                Log.Info($"[BEHAVIOR] Out-of-range color: R={col.r:F3} G={col.g:F3} B={col.b:F3}");
            }

            // Check if fully black (0,0,0) or fully white (1,1,1) — these are sometimes cheat indicators
            // but also valid player choices, so we just log them
            if (col.r < 0.01f && col.g < 0.01f && col.b < 0.01f)
                Log.Info($"[BEHAVIOR] Player using fully black color");
        }

        // ═══════════════════════════════════════════
        //  3. SCALE / SIZE ANOMALY
        //  GT uses a fixed gorilla scale. Cheats modify transform.localScale
        //  or the VRRig.scaleFactor to become tiny/giant.
        // ═══════════════════════════════════════════

        private static void CheckScale(VRRig rig, Type rigType, List<string> flags)
        {
            // Check transform scale
            Vector3 scale = rig.transform.localScale;
            if (scale.x < MinScale || scale.x > MaxScale ||
                scale.y < MinScale || scale.y > MaxScale ||
                scale.z < MinScale || scale.z > MaxScale)
            {
                flags.Add($"Abnormal Scale ({scale.x:F2},{scale.y:F2},{scale.z:F2})");
                Log.Info($"[BEHAVIOR] Abnormal scale: {scale}");
                return;
            }

            // Check scaleFactor field if it exists
            float? scaleFactor = ReadFloat(rig, rigType, "scaleFactor", "playerScale", "sizeMultiplier");
            if (scaleFactor.HasValue && (scaleFactor.Value < MinScale || scaleFactor.Value > MaxScale))
            {
                flags.Add($"Abnormal Scale Factor ({scaleFactor.Value:F2})");
                Log.Info($"[BEHAVIOR] Abnormal scaleFactor: {scaleFactor.Value}");
            }
        }

        // ═══════════════════════════════════════════
        //  4. ARM LENGTH ANOMALY
        //  Long arms cheat modifies the arm reach multiplier.
        //  GT has a fixed arm length ratio — deviations are cheating.
        // ═══════════════════════════════════════════

        private static void CheckArmLength(VRRig rig, Type rigType, List<string> flags)
        {
            // Common field names for arm length
            float? armLen = ReadFloat(rig, rigType, "maxArmLength", "armLength", "armLengthMultiplier");
            if (armLen.HasValue && (armLen.Value > MaxArmLength || armLen.Value < MinArmLength))
            {
                flags.Add($"Abnormal Arm Length ({armLen.Value:F2})");
                Log.Info($"[BEHAVIOR] Abnormal arm length: {armLen.Value}");
                return;
            }

            // Also check by measuring actual hand-to-shoulder distance via transforms
            Transform leftHand = TryGetTransform(rig, rigType, "leftHandTransform", "leftHand");
            Transform rightHand = TryGetTransform(rig, rigType, "rightHandTransform", "rightHand");
            Transform chest = TryGetTransform(rig, rigType, "chestBone", "chest", "spine");

            if (chest != null)
            {
                if (leftHand != null)
                {
                    float dist = Vector3.Distance(leftHand.position, chest.position);
                    if (dist > MaxTagDistance)
                    {
                        // Only flag if not already caught by tag distance check
                        bool alreadyFlagged = false;
                        foreach (string f in flags)
                        {
                            if (f.Contains("Long Arm Left")) { alreadyFlagged = true; break; }
                        }
                        if (!alreadyFlagged)
                            flags.Add($"Extended Left Arm ({dist:F1}m from chest)");
                    }
                }
                if (rightHand != null)
                {
                    float dist = Vector3.Distance(rightHand.position, chest.position);
                    if (dist > MaxTagDistance)
                    {
                        bool alreadyFlagged = false;
                        foreach (string f in flags)
                        {
                            if (f.Contains("Long Arm Right")) { alreadyFlagged = true; break; }
                        }
                        if (!alreadyFlagged)
                            flags.Add($"Extended Right Arm ({dist:F1}m from chest)");
                    }
                }
            }
        }

        // ═══════════════════════════════════════════
        //  5. PHOTONVIEW OWNERSHIP ANOMALY
        //  Each player's VRRig PhotonView should be owned by their actor number.
        //  If Owner != Creator, someone has stolen or spoofed the view.
        // ═══════════════════════════════════════════

        private static void CheckPhotonViewOwnership(VRRig rig, List<string> flags)
        {
            // Get the PhotonView component on the rig
            Component photonView = null;

            Component[] components = rig.GetComponents<Component>();
            foreach (Component c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "PhotonView" || typeName.Contains("PhotonView"))
                {
                    photonView = c;
                    break;
                }
            }

            if (photonView == null) return;

            Type pvType = photonView.GetType();

            // Get Owner actor number
            int ownerActorNum = -1;
            try
            {
                PropertyInfo ownerProp = pvType.GetProperty("Owner", BindingFlags.Public | BindingFlags.Instance);
                if (ownerProp != null)
                {
                    object owner = ownerProp.GetValue(photonView, null);
                    if (owner != null)
                    {
                        PropertyInfo actorProp = owner.GetType().GetProperty("ActorNumber");
                        if (actorProp != null)
                            ownerActorNum = (int)actorProp.GetValue(owner, null);
                    }
                }
            }
            catch { }

            // Get Creator/Controller actor number
            int creatorActorNum = -1;
            try
            {
                PropertyInfo creatorProp = pvType.GetProperty("CreatorActorNr", BindingFlags.Public | BindingFlags.Instance);
                if (creatorProp != null)
                {
                    object val = creatorProp.GetValue(photonView, null);
                    if (val is int n) creatorActorNum = n;
                }
            }
            catch { }

            // Also try OwnerActorNr property
            int ownerActorNr2 = -1;
            try
            {
                PropertyInfo prop = pvType.GetProperty("OwnerActorNr", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    object val = prop.GetValue(photonView, null);
                    if (val is int n) ownerActorNr2 = n;
                }
            }
            catch { }

            // Compare: if creator and owner differ, ownership was transferred (suspicious)
            if (creatorActorNum > 0 && ownerActorNum > 0 && creatorActorNum != ownerActorNum)
            {
                flags.Add($"PhotonView Ownership Mismatch (creator:{creatorActorNum} owner:{ownerActorNum})");
                Log.Warn($"[BEHAVIOR] PhotonView ownership mismatch: creator={creatorActorNum}, owner={ownerActorNum}");
            }

            // Check if ViewID is abnormally high or 0 (crafted/spoofed view)
            try
            {
                PropertyInfo vidProp = pvType.GetProperty("ViewID", BindingFlags.Public | BindingFlags.Instance);
                if (vidProp != null)
                {
                    object val = vidProp.GetValue(photonView, null);
                    if (val is int vid)
                    {
                        if (vid <= 0)
                        {
                            flags.Add("Invalid PhotonView ID (0)");
                            Log.Warn($"[BEHAVIOR] Invalid PhotonView ID: {vid}");
                        }
                    }
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════
        //  6. NAME MISMATCH DETECTION
        //  Compares the nickname displayed on the VRRig with what Photon reports.
        //  Cheats can spoof the displayed name to impersonate or hide identity.
        // ═══════════════════════════════════════════

        private static void CheckNameMismatch(VRRig rig, Type rigType, List<string> flags)
        {
            string rigName = null;
            string photonName = null;

            // Get name from VRRig display (playerNameVisible, playerText, nameTag, etc.)
            object nameObj = ReadField(rig, rigType,
                "playerNameVisible", "playerName", "nameVisible",
                "playerText", "displayName");
            if (nameObj != null)
                rigName = nameObj.ToString();

            // Get name from the Photon Player (Creator.NickName)
            try
            {
                PropertyInfo creatorProp = rigType.GetProperty("Creator",
                    BindingFlags.Public | BindingFlags.Instance);
                if (creatorProp != null)
                {
                    object creator = creatorProp.GetValue(rig, null);
                    if (creator != null)
                    {
                        PropertyInfo nickProp = creator.GetType().GetProperty("NickName");
                        if (nickProp != null)
                            photonName = nickProp.GetValue(creator, null)?.ToString();
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(rigName) && !string.IsNullOrEmpty(photonName))
            {
                // Compare (trim whitespace, case-insensitive)
                if (!rigName.Trim().Equals(photonName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add($"Name Mismatch (rig:'{rigName}' net:'{photonName}')");
                    Log.Warn($"[BEHAVIOR] Name mismatch: rig='{rigName}' photon='{photonName}'");
                }
            }
        }

        // ═══════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════

        /// <summary>
        /// Tries to find a Transform by checking multiple field/property names on the rig.
        /// </summary>
        private static Transform TryGetTransform(VRRig rig, Type rigType, params string[] names)
        {
            foreach (string name in names)
            {
                try
                {
                    FieldInfo f = rigType.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        object val = f.GetValue(rig);
                        if (val is Transform t) return t;
                    }
                }
                catch { }

                try
                {
                    PropertyInfo p = rigType.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        object val = p.GetValue(rig, null);
                        if (val is Transform t) return t;
                    }
                }
                catch { }
            }

            // Also search child transforms by name
            foreach (string name in names)
            {
                Transform found = rig.transform.Find(name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Reads a float from any of the given field names via reflection.
        /// </summary>
        private static float? ReadFloat(VRRig rig, Type rigType, params string[] names)
        {
            foreach (string name in names)
            {
                try
                {
                    FieldInfo f = rigType.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        object val = f.GetValue(rig);
                        if (val is float fv) return fv;
                        if (val is int iv) return iv;
                        if (val is double dv) return (float)dv;
                    }
                }
                catch { }

                try
                {
                    PropertyInfo p = rigType.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        object val = p.GetValue(rig, null);
                        if (val is float fv) return fv;
                        if (val is int iv) return iv;
                        if (val is double dv) return (float)dv;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Reads a field/property value from any of the given names via reflection.
        /// Returns the first non-null value found.
        /// </summary>
        private static object ReadField(VRRig rig, Type rigType, params string[] names)
        {
            foreach (string name in names)
            {
                try
                {
                    FieldInfo f = rigType.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        object val = f.GetValue(rig);
                        if (val != null) return val;
                    }
                }
                catch { }

                try
                {
                    PropertyInfo p = rigType.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        object val = p.GetValue(rig, null);
                        if (val != null) return val;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
