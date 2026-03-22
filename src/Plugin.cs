using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using Zorro.Core;
using System.Linq;

[BepInPlugin("sappykun.AntiEarlyFlare", "Anti-Early Flare", "1.2.0")]
public partial class AntiEarlyFlare : BaseUnityPlugin
{
        internal static ManualLogSource Log { get; private set; } = null!;
        internal static ConfigEntry<float>? ReachedPeakThreshold;
        internal static ConfigEntry<bool>? CheckFlareUseBeforeFinalBiome;
        internal static ConfigEntry<bool>? SmiteEarlyFlareUser;

        static protected bool shouldFreezeNextDroppedFlare = false;
        static protected Vector3 freezeNextDroppedFlarePosition;
        static protected Quaternion freezeNextDroppedFlareRotation;  
                
        private void Awake()
        {
                Log = Logger;
                ReachedPeakThreshold = Config.Bind("General", "ReachedPeakThreshold", 0.5f, new ConfigDescription(
                        "Ratio of scouts that need to be at the Peak before the flare can be used properly.",
                        new AcceptableValueRange<float>(0f, 1.0f)
                ));
                CheckFlareUseBeforeFinalBiome = Config.Bind("General", "CheckFlareUseBeforeFinalBiome", false, "If enabled, checks will trigger when a flare is used in any biome.");
                SmiteEarlyFlareUser = Config.Bind("General", "SmiteEarlyFlareUser", false, "If enabled, smites anyone trying to use the flare early.");
                Patch();
                Log.LogInfo($"Plugin AntiEarlyFlare is loaded!");
        }

        private void Patch()
        {
                Harmony val = new Harmony("HarmonyPatcher");
                val.PatchAll();
        }

        [HarmonyPatch(typeof(Flare))]
        public class FlarePatcher
        {
                private static bool HasFlareBeenActivatedEarly(Flare flare)
                {
                        bool value = flare.GetData<BoolItemData>(DataEntryKey.FlareActive).Value;

                        if (Singleton<MountainProgressHandler>.Instance == null ||
                            Singleton<PeakHandler>.Instance == null)
                                return false;

                        if (CheckFlareUseBeforeFinalBiome is { Value: true })
                                return value;

                        bool atPeak =
                                Singleton<MountainProgressHandler>.Instance.IsAtPeak(flare.item.transform.position);
                        bool heli = Singleton<PeakHandler>.Instance.summonedHelicopter;

                        return value && atPeak && !heli;
                }

                private static bool ShouldBlockFlare()
                {
                        List<Character> characterList = new List<Character>();
                        foreach (Character chara in Character.AllCharacters)
                        {
                                if (!chara.data.dead)
                                {
                                        characterList.Add(chara);
                                }
                        }

                        List<Character> nearbyCharacters = new List<Character>();

                        foreach (Character chara in characterList)
                        {
                                if (Singleton<MountainProgressHandler>.Instance.IsAtPeak(chara.Center))
                                {
                                        nearbyCharacters.Add(chara);
                                }
                        }
                        
                        return nearbyCharacters.Count / (float)characterList.Count < ReachedPeakThreshold?.Value;
                }

                [HarmonyPrefix]
                [HarmonyPatch("Update")]
                public static bool BlockFlareActivation(Flare __instance)
                {
                        if (!PhotonNetwork.IsMasterClient)
                                return true;

                        if (HasFlareBeenActivatedEarly(__instance) && ShouldBlockFlare())
                        {
                                Log.LogInfo($"{__instance.item.holderCharacter?.characterName} tried to light the flare early!");

                                if (__instance.item.holderCharacter && SmiteEarlyFlareUser is { Value: true })
                                {
                                        var rb = __instance.item.GetComponent<Rigidbody>();
                                        if (rb)
                                        {
                                                freezeNextDroppedFlarePosition = rb.position;
                                                freezeNextDroppedFlareRotation = rb.rotation;
                                                shouldFreezeNextDroppedFlare = true;
                                        }

                                        Log.LogInfo($"{__instance.item.holderCharacter?.characterName} is being slain...");

                                        __instance.item.holderCharacter?.view.RPC("RPCA_Die", RpcTarget.All, new object[]
                                        {
                                                __instance.item.holderCharacter.Center + Vector3.up * 0.2f +
                                                Vector3.forward * 0.1f
                                        });
                                }
                                
                                __instance.GetData<BoolItemData>(DataEntryKey.FlareActive).Value = false;
                                __instance.item.SetUseRemainingPercentage(1.00f);
                                
                                OptionableIntItemData data =
                                        __instance.item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
                                data.HasData = true;
                                data.Value = 1;
                                
                                return false;
                        }
                        return true;
                }

                [HarmonyPrefix]
                [HarmonyPatch("SetFlareLitRPC")]
                public static bool BlockFlareRPC(Flare __instance)
                {
                        if (!PhotonNetwork.IsMasterClient)
                                return true;
                        return !(HasFlareBeenActivatedEarly(__instance) && ShouldBlockFlare());
                }
                
                [HarmonyPrefix]
                [HarmonyPatch("TriggerHelicopter")]
                public static bool BlockHelicopterRPC(Flare __instance)
                {
                        if (!PhotonNetwork.IsMasterClient)
                                return true;
                        return !(ShouldBlockFlare());
                }
        }


        [HarmonyPatch(typeof(Item))]
        public class ItemPatcher
        {
                [HarmonyPostfix]
                [HarmonyPatch("SetItemInstanceDataRPC")]
                public static void FreezeFlare(Item __instance, ItemInstanceData instanceData)
                {
                        Flare? flare = __instance.itemComponents.FirstOrDefault(x => x is Flare) as Flare;
                        if (flare != null && shouldFreezeNextDroppedFlare)
                        {
                                Log.LogInfo($"Freezing flare in place...");

                                var rb = flare.GetComponent<Rigidbody>();
                                if (rb)
                                {
                                        rb.isKinematic = false;
                                        rb.linearVelocity = Vector3.zero;
                                        rb.angularVelocity = Vector3.zero;
                                        rb.constraints = RigidbodyConstraints.FreezeAll;
                                        rb.position = freezeNextDroppedFlarePosition;
                                        rb.rotation = freezeNextDroppedFlareRotation;
                                        flare.item.transform.position = freezeNextDroppedFlarePosition;
                                        flare.item.transform.rotation = freezeNextDroppedFlareRotation;
                                }

                                Log.LogDebug($"Flare position is {freezeNextDroppedFlarePosition}");
                                Log.LogDebug($"Flare rotation is {freezeNextDroppedFlareRotation}");

                                flare.GetData<BoolItemData>(DataEntryKey.FlareActive).Value = false;
                                flare.item.SetUseRemainingPercentage(1.00f);
                                
                                OptionableIntItemData data =
                                        flare.item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
                                data.HasData = true;
                                data.Value = 1;

                                shouldFreezeNextDroppedFlare = false;    
                        }
                }
        }
}
