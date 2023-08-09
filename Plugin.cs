using System.Reflection;
using Aki.Reflection.Patching;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DeadzoneMod;

public struct WeaponSettingsOverrides {
    public bool Enabled;
    public float Position;
    public float Sensitivity;
    public float MaxAngle;
    public float AimMultiplier;

    public WeaponSettingsOverrides(
        bool Enabled = true,
        float Position = 0.2f,
        float Sensitivity = 0.25f,
        float MaxAngle = 5.0f,
        float AimMultiplier = 0.0f
    ) {
        this.Enabled = Enabled;
        this.Position = Position;
        this.Sensitivity = Sensitivity;
        this.MaxAngle = MaxAngle;
        this.AimMultiplier = AimMultiplier;
    }
}

public struct WeaponSettings {
    public ConfigEntry<bool> Enabled;
    public ConfigEntry<float> Position;
    public ConfigEntry<float> Sensitivity;
    public ConfigEntry<float> MaxAngle;
    public ConfigEntry<float> AimMultiplier;
    public WeaponSettings(
        ConfigFile Config,
        WeaponSettingsOverrides settings,
        string GroupName = "Group"
    ) {
        string group = $"{GroupName}s";
        Enabled = Config.Bind(group, $"{GroupName} deadzone enabled", settings.Enabled, new ConfigDescription("Will deadzone be enabled"));
        Position = Config.Bind(group, $"{GroupName} deadzone pivot", settings.Position, new ConfigDescription("How far back will the deadzone pivot"));
        Sensitivity = Config.Bind(group, $"{GroupName} deadzone sensitivity", settings.Sensitivity, new ConfigDescription("How fast will the gun move (less = slower)"));
        MaxAngle = Config.Bind(group, $"{GroupName} max deadzone angle", settings.MaxAngle, new ConfigDescription("How much will the gun be able to move (degrees)"));
        AimMultiplier = Config.Bind(group, $"{GroupName} aiming deadzone multiplier", settings.AimMultiplier, new ConfigDescription("How much deadzone will there be while aiming (0 = none)"));;
    }
}

public struct WeaponSettingsGroup {
    public WeaponSettings pistol;
    public WeaponSettings fallback;    
    public WeaponSettingsGroup(WeaponSettings fallback, WeaponSettings pistol) {
        this.fallback = fallback;
        this.pistol = pistol;
    }
    
    public WeaponSettings this[string index] {
        get => (index == "pistol" ? pistol : fallback); // lol amazing
    }
}

public struct PluginSettings {
    public bool Initialized;
    public ConfigEntry<bool> Enabled;
    public WeaponSettingsGroup WeaponSettings;
}

[BepInPlugin("org.bepinex.plugins.deadzonemod", "DeadzoneMod", "1.0.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static PluginSettings Settings = new();

    void Awake()
    {
        Settings.Enabled = Config.Bind("Values", "Global deadzone enabled", true, new ConfigDescription("Will deadzone be enabled for any group"));

        Settings.WeaponSettings = new WeaponSettingsGroup(
            new WeaponSettings(
                Config,
                new WeaponSettingsOverrides( // no idea why this being empty breaks it
                    true,
                    0.2f
                ),
                "Default"
            ),
            new WeaponSettings(
                Config,
                new WeaponSettingsOverrides(
                    true,
                    0.0f
                ),
                "Pistol"
            )
        );

        Settings.Initialized = true;
        new DeadzonePatch().Enable();
    }
}
public class DeadzonePatch : ModulePatch
{
    static Vector2 lastYawPitch;
    static float cumulativePitch = 0f;
    static float cumulativeYaw = 0f;

    static float aimLerp = 0f;
    static System.Diagnostics.Stopwatch aimWatch = new();

    protected override MethodBase GetTargetMethod()
        => typeof(EFT.Animations.ProceduralWeaponAnimation)
            .GetMethod("AvoidObstacles", BindingFlags.Instance | BindingFlags.Public);

    [PatchPostfix]
    static void PostFix(EFT.Animations.ProceduralWeaponAnimation __instance, EFT.Player.FirearmController ___firearmController_0)
    {
        if (!Plugin.Settings.Enabled.Value) return;

        if (!___firearmController_0) return;
        
        EFT.Player _player = (EFT.Player)AccessTools.Field(typeof(EFT.Player.ItemHandsController), "_player").GetValue(___firearmController_0);
        if (_player.IsAI) return;


        // Degrees, yaw pitch
        Vector2 currentYawPitch = new Vector2(_player.MovementContext.Yaw, _player.MovementContext.Pitch);

        Quaternion lastRotation = Quaternion.Euler(lastYawPitch.x, lastYawPitch.y, 0);
        Quaternion currentRotation = Quaternion.Euler(currentYawPitch.x, currentYawPitch.y, 0);

        WeaponSettings settings = Plugin.Settings.WeaponSettings[___firearmController_0.Item.WeapClass];

        // all euler angles should go to hell
        lastRotation = Quaternion.SlerpUnclamped(currentRotation, lastRotation, settings.Sensitivity.Value);

        Vector3 delta = (currentRotation * Quaternion.Inverse(lastRotation)).eulerAngles;

        cumulativeYaw += delta.x;
        cumulativePitch += delta.y;

        if (cumulativePitch < 0)
            cumulativePitch = cumulativePitch + 360; // dont feel like doing this properly thanks

        if (cumulativeYaw < 0)
            cumulativeYaw = cumulativeYaw + 360;

        cumulativeYaw %= 360;
        cumulativePitch %= 360;

        if (cumulativePitch > 180)
            cumulativePitch = cumulativePitch - 360;

        if (cumulativeYaw > 180)
            cumulativeYaw = cumulativeYaw - 360;

        lastYawPitch = currentYawPitch;

        Vector2 tempVector = new Vector2(cumulativeYaw, cumulativePitch);

        if (tempVector.magnitude > settings.MaxAngle.Value)
        {
            tempVector = tempVector.normalized * settings.MaxAngle.Value;

            cumulativeYaw = tempVector.x;
            cumulativePitch = tempVector.y;
        }

        float dt = aimWatch.Elapsed.Milliseconds / 1000f;

        aimWatch.Reset();
        aimWatch.Start();

        aimLerp = Mathf.Lerp(aimLerp, __instance.IsAiming ? 1f : 0f, dt * 6f);

        float aimMult = 1f - ((1f - settings.AimMultiplier.Value) * aimLerp);

        __instance.HandsContainer.WeaponRootAnim.LocalRotateAround(
            Vector3.up * settings.Position.Value,
            new Vector3(
                cumulativePitch * aimMult,
                0,
                cumulativeYaw * aimMult
            )
        );
    }
}
