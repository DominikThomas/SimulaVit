using UnityEngine;

public class RandomMusicPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip[] tracks;

    private int lastTrackIndex = -1;
    private static RandomMusicPlayer instance;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        if (!audioSource.isPlaying)
            PlayRandomTrack();
    }

    void Update()
    {
        if (!audioSource.isPlaying && tracks != null && tracks.Length > 0)
        {
            PlayRandomTrack();
        }
    }

    void PlayRandomTrack()
    {
        if (tracks == null || tracks.Length == 0)
            return;

        int newIndex;

        if (tracks.Length == 1)
        {
            newIndex = 0;
        }
        else
        {
            do
            {
                newIndex = Random.Range(0, tracks.Length);
            }
            while (newIndex == lastTrackIndex);
        }

        lastTrackIndex = newIndex;
        audioSource.clip = tracks[newIndex];
        audioSource.Play();
    }
}