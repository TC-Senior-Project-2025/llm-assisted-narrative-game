using System;
using UnityEngine;

namespace Game.Services.Sounds
{
    public class SfxService : MonoBehaviour
    {
        public static SfxService Main { get; private set; }
        private AudioSource _audioSource;

        [Serializable]
        public struct Preset
        {
            public AudioClip clip;
            public float volume;

            public Preset(AudioClip clip, float volume = 1f)
            {
                this.clip = clip;
                this.volume = volume;
            }
        }

        [Header("UI")]
        public Preset click;

        [Header("Army")]
        public Preset armyUnitMove;

        [Header("Diplomacy")]
        public Preset swordsClash;

        private void Awake()
        {
            Main = this;
            _audioSource = GetComponent<AudioSource>();
        }

        public void Play(AudioClip clip, float volume = 1f)
        {
            _audioSource.PlayOneShot(clip, volume);
        }

        public void Play(Preset preset, float? overrideVolume = null)
        {
            Play(preset.clip, overrideVolume ?? preset.volume);
        }
    }
}
