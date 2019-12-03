using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TimerScript : MonoBehaviour
{
    public Text BlackTimerText;
    public Text WhiteTimerText;
    private float startBlackTime;
    private float startWhiteTime;

    void Start()
    {
        startWhiteTime = Time.time;
        startBlackTime = Time.time;
    }

    void Update()
    {
        float tw = Time.time - startWhiteTime;
        float tb = Time.time - startBlackTime;
        string minutesWhite = ((int)tw / 60).ToString();
        string secoundsWhite = (tw % 60).ToString("f2");
        string minutesBlack = ((int)tw / 60).ToString();
        string secoundsBlack = (tb % 60).ToString("f2");
        WhiteTimerText.text = minutesWhite + ":" + secoundsWhite;
        BlackTimerText.text = minutesBlack + ":" + secoundsBlack;
    }
}
        /*
        public Text BlackTimerText;
        public Text WhiteTimerText;
        private float startBlackTime;
        private float startWhiteTime;

        void Start()
        {
        startWhiteTime = Time.time;
        startBlackTime = Time.time;
        }
        void update()
        {
        float tw = Time.time - startWhiteTime;
        float tb = Time.time - startBlackTime;
        string minutesWhite = ((int)tw / 60).ToString();
        string secoundsWhite = (tw % 60).ToString("f2");
        string minutesBlack = ((int)tw / 60).ToString();
        string secoundsBlack = (tb % 60).ToString("f2");
        WhiteTimerText.text = minutesWhite + ":" + secoundsWhite;
        BlackTimerText.text = minutesBlack + ":" + secoundsBlack;
        }
        */