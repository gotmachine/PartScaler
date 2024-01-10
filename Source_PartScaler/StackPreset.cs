using System;
using System.Collections.Generic;

namespace PartScaler
{
    public class StackPreset
    {
        public static StackPreset[] presets;
        public static int presetsCount;

        static StackPreset()
        {
            presets = new StackPreset[]
            {
                    new StackPreset(0.3125f, false),
                    new StackPreset(0.625f, true),
                    new StackPreset(0.9375f, false), // not a multiple, but commonly used
                    new StackPreset(1.25f, true),
                    new StackPreset(1.875f, true),
                    new StackPreset(2.5f, true),
                    new StackPreset(3.75f, true),
                    new StackPreset(4.375f, false),
                    new StackPreset(5f, true),
                    new StackPreset(6.25f, false),
                    new StackPreset(7.5f, true),
                    new StackPreset(10f, true),
                    new StackPreset(12.5f, false),
                    new StackPreset(15f, false),
                    new StackPreset(20f, false)
            };

            Array.Sort(presets, (a, b) => a.size.CompareTo(b.size));

            presetsCount = presets.Length;
            for (int i = 0; i < presetsCount; i++)
                presets[i].index = i;

            presets[0].minSize = 0f;
            for (int i = 1; i < presetsCount; i++)
                presets[i].minSize = presets[i].size - ((presets[i].size - presets[i - 1].size) * 0.5f);
        }

        public static StackPreset GetClosestStackPreset(float stackSize)
        {
            for (int i = 0; i < presetsCount - 1; i++)
                if (stackSize > presets[i].minSize && stackSize < presets[i + 1].minSize)
                    return presets[i];

            return presets[presetsCount - 1];
        }

        public static void GetPresets(float min, float max, float current, out string[] presetProfiles, out string[] presetTitles, out int currentIdx)
        {
            int minPresetIdx = -1;
            int presetCount = 0;
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i].size >= min)
                {
                    if (minPresetIdx == -1)
                        minPresetIdx = i;

                    if (presets[i].size <= max)
                        presetCount++;
                }
            }

            presetProfiles = new string[presetCount];
            presetTitles = new string[presetCount];
            currentIdx = 0;
            for (int i = 0; i < presetCount; i++)
            {
                StackPreset stackPreset = StackPreset.presets[minPresetIdx + i];
                presetProfiles[i] = stackPreset.profile;
                presetTitles[i] = stackPreset.title;
                if (stackPreset.size == current)
                    currentIdx = i;
            }
        }

        public static StackPreset GetPresetForProfile(string profile)
        {
            int idx = presetsCount;
            while (idx-- > 0)
                if (presets[idx].profile == profile)
                    return presets[idx];

            throw new KeyNotFoundException($"The profile {profile} doesn't exists");
        }

        public float size;
        public float minSize;
        public int index;
        public bool isAutoProfile;
        public string profile;
        public string title;

        public StackPreset(float size, bool isAutoProfile = false, string profile = null)
        {
            this.size = size;
            this.isAutoProfile = isAutoProfile;

            if (profile == null)
                profile = Math.Round(size / 1.25f, 3).ToString("0.###");

            this.profile = profile;
            title = size.ToString("0.####m");
        }

        public override string ToString() => title;
    }
}
