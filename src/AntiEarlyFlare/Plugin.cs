using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using Zorro.Core;

[BepInAutoPlugin]
public partial class AntiEarlyFlare : BaseUnityPlugin
{
        internal static ManualLogSource Log { get; private set; } = null!;
        internal static ConfigEntry<float>? ReachedPeakThreshold;
        internal static ConfigEntry<bool>? SmiteEarlyFlareUser;
                
        private void Awake()
        {
                Log = Logger;
                ReachedPeakThreshold = Config.Bind("General", "ReachedPeakThreshold", 0.5f, new ConfigDescription(
                        "Ratio of scouts that need to be at the Peak before the flare can be used properly.",
                        new AcceptableValueRange<float>(0f, 1.0f)
                ));
                SmiteEarlyFlareUser = Config.Bind("General", "SmiteEarlyFlareUser", false, "If enabled, smites anyone trying to use the flare early.");
                Patch();
                Log.LogInfo($"Plugin {Name} is loaded!");
        }

        private void Patch()
        {
                Harmony val = new Harmony("HarmonyPatcher");
                val.PatchAll();
        }

        [HarmonyPatch(typeof(Flare))]
        public class FlarePatcher
        {
                private static bool HasFlareBeenActivatedAtPeak(Flare flare)
                {
                        bool value = flare.GetData<BoolItemData>(DataEntryKey.FlareActive).Value;
                        Character ch = flare.item.holderCharacter;

                        if (!ch)
                                return false;

                        if (Singleton<MountainProgressHandler>.Instance == null ||
                            Singleton<PeakHandler>.Instance == null)
                                return false;

                        bool atPeak =
                                Singleton<MountainProgressHandler>.Instance.IsAtPeak(flare.item.holderCharacter.Center);
                        bool heli = Singleton<PeakHandler>.Instance.summonedHelicopter;

                        return value && ch && atPeak && !heli;
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
                        
                        return (nearbyCharacters.Count / (float)characterList.Count < ReachedPeakThreshold?.Value);
                }

                [HarmonyPrefix]
                [HarmonyPatch("Update")]
                public static bool BlockFlareActivation(Flare __instance)
                {
                        if (!PhotonNetwork.IsMasterClient)
                                return true;

                        if (HasFlareBeenActivatedAtPeak(__instance) && ShouldBlockFlare())
                        {
                                if (SmiteEarlyFlareUser is { Value: true })
                                {
                                        __instance.item.holderCharacter.view.RPC("RPCA_Die", RpcTarget.All, new object[]
                                        {
                                                __instance.item.holderCharacter.Center + Vector3.up * 0.2f +
                                                Vector3.forward * 0.1f
                                        });
                                        
                                        /* TODO: freeze the flare in the user's hands
                                        var rb = flare.GetComponent<Rigidbody>();
                                        if (rb)
                                        {
                                                rb.isKinematic = false;
                                                rb.linearVelocity = Vector3.zero;
                                                rb.angularVelocity = Vector3.zero;
                                                rb.constraints = RigidbodyConstraints.FreezeAll;
                                        }
                                        */
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
                        return !(HasFlareBeenActivatedAtPeak(__instance) && ShouldBlockFlare());
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
}