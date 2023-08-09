using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using EFT;
using EFT.Animations;

namespace DeadzoneMod;

public struct WeaponSettingsOverrides
{
    public bool Enabled;
    public bool UseDefault;
    public float Position;
    public float Sensitivity;
    public float MaxAngle;
    public float AimMultiplier;

    public WeaponSettingsOverrides(
        bool Enabled = true,
        bool UseDefault = false,
        float Position = 0.1f,
        float Sensitivity = 0.25f,
        float MaxAngle = 5.0f,
        float AimMultiplier = 0.0f
    )
    {
        this.Enabled = Enabled;
        this.UseDefault = UseDefault;
        this.Position = Position;
        this.Sensitivity = Sensitivity;
        this.MaxAngle = MaxAngle;
        this.AimMultiplier = AimMultiplier;
    }
}

public struct WeaponSettings
{
    public ConfigEntry<bool> Enabled;

    public readonly bool UseDefault => UseDefaultConfig.Value;
    public readonly ConfigEntry<bool> UseDefaultConfig;
    public readonly ConfigEntry<float> Position;
    public readonly ConfigEntry<float> Sensitivity;
    public readonly ConfigEntry<float> MaxAngle;
    public readonly ConfigEntry<float> AimMultiplier;
    public WeaponSettings(
        ConfigFile Config,
        WeaponSettingsOverrides settings,
        string GroupName = "Group"
    )
    {
        string group = $"{GroupName}s";
        Enabled = Config.Bind(group, $"{GroupName} deadzone enabled", settings.Enabled, new ConfigDescription("Will deadzone be enabled"));
        UseDefaultConfig = GroupName == "Default" ? null : Config.Bind(group, $"{GroupName} disable", settings.UseDefault, new ConfigDescription("Will this group use default values instead"));
        Position = Config.Bind(group, $"{GroupName} deadzone pivot", settings.Position, new ConfigDescription("How far back will the deadzone pivot"));
        Sensitivity = Config.Bind(group, $"{GroupName} deadzone sensitivity", settings.Sensitivity, new ConfigDescription("How fast will the gun move (less = slower)"));
        MaxAngle = Config.Bind(group, $"{GroupName} max deadzone angle", settings.MaxAngle, new ConfigDescription("How much will the gun be able to move (degrees)"));
        AimMultiplier = Config.Bind(group, $"{GroupName} aiming deadzone multiplier", settings.AimMultiplier, new ConfigDescription("How much deadzone will there be while aiming (0 = none)")); ;
    }
}

public struct WeaponSettingsGroup
{
    readonly Dictionary<string, WeaponSettings> settings = new();
    public WeaponSettings fallback;
    public WeaponSettingsGroup(WeaponSettings fallback)
    {
        this.fallback = fallback;
    }

    public readonly WeaponSettings this[string index]
    {
        get
        {
            if (settings.TryGetValue(index, out WeaponSettings chosen))
                return chosen;

            return fallback;
        }
        set => settings.Add(index, value);
    }
}

public struct PluginSettings
{
    public bool Initialized;
    public ConfigEntry<bool> Enabled;
    public WeaponSettingsGroup WeaponSettings;
}

[BepInPlugin("me.lualeet.deadzone", "Deadzone", "1.0.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static PluginSettings Settings = new();
    public static bool Enabled => Settings.Enabled != null && Settings.Enabled.Value;

    void Awake()
    {
        Settings.Enabled = Config.Bind("Values", "Global deadzone enabled", true, new ConfigDescription("Will deadzone be enabled for any group"));

        Settings.WeaponSettings = new WeaponSettingsGroup(
            new WeaponSettings(
                Config,
                new WeaponSettingsOverrides( // no idea why this being empty breaks it
                    Position: 0.1f
                ),
                "Default"
            )
        );

        Settings.Initialized = true;
        DeadzonePatch.Enable();
    }

    public void Log(object data)
    {
        Logger.LogWarning(data);
    }
}

public class DeadzonePatch
{
    static Quaternion MakeQuaternionDelta(Quaternion from, Quaternion to)
        => to * Quaternion.Inverse(from);

    static void SetRotationWrapped(ref float yaw, ref float pitch)
    {
        // I prefer using (-180; 180) euler angle range over (0; 360)
        // However, wrapping the angles is easier with (0; 360), so temporarily cast it
        if (yaw < 0) yaw += 360;
        if (pitch < 0) pitch += 360;

        pitch %= 360;
        yaw %= 360;

        // Now cast it back
        if (yaw > 180) yaw -= 360;
        if (pitch > 180) pitch -= 360;
    }

    static void SetRotationClamped(ref float yaw, ref float pitch, float maxAngle)
    {
        Vector2 clampedVector
            = Vector2.ClampMagnitude(
                new Vector2(yaw, pitch),
                maxAngle
            );

        yaw = clampedVector.x;
        pitch = clampedVector.y;
    }

    static readonly System.Diagnostics.Stopwatch aimWatch = new();
    static float GetDeltaTime()
    {
        float deltaTime = aimWatch.Elapsed.Milliseconds / 1000f;
        aimWatch.Reset();
        aimWatch.Start();

        return deltaTime;
    }

    static float aimSmoothed = 0f;

    static void UpdateAimSmoothed(ProceduralWeaponAnimation animationInstance)
    {
        float deltaTime = GetDeltaTime();

        // TODO: use aiming time
        // Maybe it can be extracted from ProceduralWeaponAnimation?
        aimSmoothed = Mathf.Lerp(aimSmoothed, animationInstance.IsAiming ? 1f : 0f, deltaTime * 6f);
    }

    static float cumulativePitch = 0f;
    static float cumulativeYaw = 0f;

    static Vector2 lastYawPitch;

    static void UpdateDeadzoneRotation(Vector2 currentYawPitch, WeaponSettings settings)
    {
        Quaternion lastRotation = Quaternion.Euler(lastYawPitch.x, lastYawPitch.y, 0);
        Quaternion currentRotation = Quaternion.Euler(currentYawPitch.x, currentYawPitch.y, 0);

        lastYawPitch = currentYawPitch;

        // all euler angles should go to hell
        lastRotation = Quaternion.SlerpUnclamped(currentRotation, lastRotation, settings.Sensitivity.Value);

        Vector3 delta = MakeQuaternionDelta(lastRotation, currentRotation).eulerAngles;

        cumulativeYaw += delta.x;
        cumulativePitch += delta.y;

        SetRotationWrapped(ref cumulativeYaw, ref cumulativePitch);

        SetRotationClamped(ref cumulativeYaw, ref cumulativePitch, settings.MaxAngle.Value);
    }

    static void ApplyDeadzone(ProceduralWeaponAnimation animationInstance, WeaponSettings settings)
    {
        float aimMultiplier = 1f - ((1f - settings.AimMultiplier.Value) * aimSmoothed);

        Transform weaponRootAnim = animationInstance.HandsContainer.WeaponRootAnim;

        if (weaponRootAnim == null) return;

        weaponRootAnim.LocalRotateAround(
            Vector3.up * settings.Position.Value,
            new Vector3(
                cumulativePitch * aimMultiplier,
                0,
                cumulativeYaw * aimMultiplier
            )
        );

        // Not doing this messes up pivot for all offsets after this
        weaponRootAnim.LocalRotateAround(
            Vector3.up * -settings.Position.Value,
            Vector3.zero
        );
    }

    static void PatchedUpdate(Player player, ProceduralWeaponAnimation weaponAnimation)
    {
        Vector2 currentYawPitch = new(player.MovementContext.Yaw, player.MovementContext.Pitch);

        WeaponSettings settings;

        settings = Plugin.Settings.WeaponSettings.fallback;

        UpdateDeadzoneRotation(currentYawPitch, settings);

        UpdateAimSmoothed(weaponAnimation);

        ApplyDeadzone(weaponAnimation, settings);
    }

    // BepInEx

    static public void Enable()
    {
        Harmony.CreateAndPatchAll(typeof(DeadzonePatch));
    }

    [HarmonyPatch(typeof(Player), "VisualPass")]
    [HarmonyPrefix]
    static void FindLocalProceduralWeaponAnimation(Player __instance)
    {
        if (!Plugin.Enabled) return;

        if (!__instance.IsYourPlayer) return;

        localPlayer = __instance;
        localWeaponAnimation = __instance.ProceduralWeaponAnimation;
    }

    static Player localPlayer;
    static ProceduralWeaponAnimation localWeaponAnimation;

    [HarmonyPatch(typeof(ProceduralWeaponAnimation), "AvoidObstacles")]
    [HarmonyPostfix]
    static void WeaponAnimationPatch(ProceduralWeaponAnimation __instance)
    {
        if (!Plugin.Enabled) return;

        if (localPlayer == null) return;
        if (__instance != localWeaponAnimation) return;

        PatchedUpdate(localPlayer, localWeaponAnimation);
    }
}
