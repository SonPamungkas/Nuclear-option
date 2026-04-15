using BepInEx;
using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System;
using System.Text;
using System.Linq;

namespace DeathLaser
{
    [BepInPlugin("com.death.laser.mod", "Death Laser Overdrive Pipeline", "8.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("Death Laser Overdrive Pipeline starting...");
            var harmony = new Harmony("com.death.laser.mod");

            try
            {
                // Hook our Custom Laser Damage Pipeline dynamically to avoid Namespace failures with PersistentID
                var asm = typeof(Laser).Assembly;
                var unitPartType = asm.GetTypes().FirstOrDefault(t => t.Name == "UnitPart");
                
                if (unitPartType != null)
                {
                    var takeDamageMethod = unitPartType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(m => m.Name == "TakeDamage");
                    if (takeDamageMethod != null)
                    {
                        var prefix = new HarmonyMethod(AccessTools.Method(typeof(VirtualLaserPipeline), "Prefix"));
                        harmony.Patch(takeDamageMethod, prefix: prefix);
                        Logger.LogInfo("-> SUCCESSFULLY HIJACKED UnitPart.TakeDamage()");
                    }
                }

                harmony.PatchAll(); // Activates Laser_Injector_Patch
                Logger.LogInfo("Death Laser Pipeline Fully Active. The Unity Physics Engine has been bypassed.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Pipeline Hook failed: {ex}");
            }
        }
    }

    // THE HIJACK: We intercept TakeDamage dynamically
    public static class VirtualLaserPipeline
    {
        public static bool Prefix(object __instance, object[] __args)
        {
            try
            {
                // Find fireDamage or blastDamage parameter (indices 1 and 3 usually, but let's check all looking for HUGE numbers)
                bool isLaserHit = false;
                foreach (var arg in __args)
                {
                    if (arg is float dmgVal && dmgVal > 900000f) // The signature from Laser_Injector_Patch!
                    {
                        isLaserHit = true;
                        break;
                    }
                }

                if (isLaserHit)
                {
                    // WE FOUND A LASER SIZZLE. BYPASS ALL GAME LOGIC!
                    var traverse = Traverse.Create(__instance);

                    // 1. Instantly Crush The Health (this circumvents DamageAtRange and ArmorProperties matrices)
                    if (traverse.Field("health").FieldExists()) traverse.Field("health").SetValue(0f);
                    if (traverse.Field("localHealth").FieldExists()) traverse.Field("localHealth").SetValue(0f);
                    if (traverse.Field("hitPoints").FieldExists()) traverse.Field("hitPoints").SetValue(0f);

                    // 2. Invoke the Joint Breaking Protocols manually
                    // If action delegates exist, trigger them!
                    try
                    {
                        // Invoke ApplyDamage with impossible net values
                        var applyMethod = __instance.GetType().GetMethod("ApplyDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (applyMethod != null)
                        {
                            applyMethod.Invoke(__instance, new object[] { 1000000f, 1000000f, 1000000f, 0f });
                        }

                        // Try to execute DetachDamageParticles
                        var detachMethod = __instance.GetType().GetMethod("DetachDamageParticles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (detachMethod != null) detachMethod.Invoke(__instance, null);
                        
                        // Force the structure joints apart
                        var onPartDetached = traverse.Field("onPartDetached").GetValue<Delegate>();
                        if (onPartDetached != null) onPartDetached.DynamicInvoke(__instance);

                        var onJointBroken = traverse.Field("onJointBroken").GetValue<Delegate>();
                        if (onJointBroken != null) onJointBroken.DynamicInvoke(__instance);
                    }
                    catch { }

                    // Allow the native method to STILL return so the Network Indexer gets the kill log, 
                    // but since HP is already totally zeroed out, it processes immediate death.
                }
            }
            catch { }

            return true;
        }
    }

    // This patch force-feeds the Laser the huge value EVERY Active Frame so it slips past the WeaponManager instantiation reset
    [HarmonyPatch(typeof(Laser), "FixedUpdate")]
    public static class Laser_Injector_Patch
    {
        public static void Prefix(Laser __instance)
        {
            var traverse = Traverse.Create(__instance);
            if (traverse.Field("fireCommanded").FieldExists() && traverse.Field("fireCommanded").GetValue<bool>())
            {
                // FORCE FEED THE NUCLEAR PAYLOAD EVERY FRAME
                if (traverse.Field("fireDamage").FieldExists()) traverse.Field("fireDamage").SetValue(1000000f);
                if (traverse.Field("blastDamage").FieldExists()) traverse.Field("blastDamage").SetValue(1000000f);
                if (traverse.Field("pierceDamage").FieldExists()) traverse.Field("pierceDamage").SetValue(1000000f); // Make sure this punches through armor too
            }
        }
    }
}
