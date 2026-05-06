using System;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [System.Serializable]
    public enum EnvironmentType
    {
        Color,
        Skybox,
        Chromakey,
    }

    [System.Serializable]
    public struct HSVColor
    {
        public float hue;
        public float saturation;
        public float value;

        public HSVColor(float h, float s, float v)
        {
            hue = h;
            saturation = s;
            value = v;
        }

        public Color ToRGB()
        {
            return Color.HSVToRGB(hue / 360f, saturation / 100f, value / 100f);
        }

        public static HSVColor FromRGB(Color rgb)
        {
            Color.RGBToHSV(rgb, out float h, out float s, out float v);
            return new HSVColor(h * 360f, s * 100f, v * 100f);
        }
    }


    public interface ISkyboxData
    {
        public string name { get; }

        public Material material { get; }

        public Texture2D image { get; }
    }    

    public interface IEnvironmentProvider
    {
        EnvironmentType environmentType { get; }

        Color backgroundColor { get; }

        HSVColor backgroundHSV { get; }

        ISkyboxData[] skyboxes { get; }

        int skyboxIndex { get; }

        Material[] skyboxMaterials { get; }

        void SetEnvironmentType(EnvironmentType environmentType);

        void SetEnvironmentBackground(int index);

        void SetEnvironmenteBackgroundColor(Color color);

        void SetEnvironmentBackgroundHSV(HSVColor hsvColor);
    }

    public static class EnvironmentService
    {
        public static Action onEnvironmentChanged;

        public static EnvironmentType environmentType => SingletonService<IEnvironmentProvider>.subject?.environmentType ?? EnvironmentType.Color;

        public static Color environmentBackgroundColor => SingletonService<IEnvironmentProvider>.subject?.backgroundColor ?? Color.clear;

        public static HSVColor environmentBackgroundHSV => SingletonService<IEnvironmentProvider>.subject?.backgroundHSV ?? new HSVColor();

        public static int environmentSkyboxIndex => SingletonService<IEnvironmentProvider>.subject?.skyboxIndex ?? -1;

        public static Material[] environmentSkyboxMaterials => SingletonService<IEnvironmentProvider>.subject?.skyboxMaterials ?? new Material[0];

        public static void SetEnvironmentType(EnvironmentType environmentType)
        {
            Service<IEnvironmentProvider>.subjects.ForEach(s => s.SetEnvironmentType(environmentType));
        }

        public static void SetEnvironmentBackground(int index)
        {
            Service<IEnvironmentProvider>.subjects.ForEach(s => s.SetEnvironmentBackground(index));
        }

        public static void SetEnvironmentBackgroundColor(Color color)
        {
            Service<IEnvironmentProvider>.subjects.ForEach(s => s.SetEnvironmenteBackgroundColor(color));
        }

        public static void SetEnvironmentBackgroundHSV(HSVColor hsvColor)
        {
            Service<IEnvironmentProvider>.subjects.ForEach(s => s.SetEnvironmentBackgroundHSV(hsvColor));
        }
    }
}