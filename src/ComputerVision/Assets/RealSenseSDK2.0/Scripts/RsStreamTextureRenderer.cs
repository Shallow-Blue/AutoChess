using Intel.RealSense;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

public class RsStreamTextureRenderer : MonoBehaviour
{
    private static TextureFormat Convert(Format lrsFormat)
    {
        switch (lrsFormat)
        {
            case Format.Z16: return TextureFormat.R16;
            case Format.Disparity16: return TextureFormat.R16;
            case Format.Rgb8: return TextureFormat.RGB24;
            case Format.Rgba8: return TextureFormat.RGBA32;
            case Format.Bgra8: return TextureFormat.BGRA32;
            case Format.Y8: return TextureFormat.Alpha8;
            case Format.Y16: return TextureFormat.R16;
            case Format.Raw16: return TextureFormat.R16;
            case Format.Raw8: return TextureFormat.Alpha8;
            case Format.Disparity32: return TextureFormat.RFloat;
            case Format.Yuyv:
            case Format.Bgr8:
            case Format.Raw10:
            case Format.Xyz32f:
            case Format.Uyvy:
            case Format.MotionRaw:
            case Format.MotionXyz32f:
            case Format.GpioRaw:
            case Format.Any:
            default:
                throw new ArgumentException(string.Format("librealsense format: {0}, is not supported by Unity", lrsFormat));
        }
    }

    private static int BPP(TextureFormat format)
    {
        switch (format)
        {
            case TextureFormat.ARGB32:
            case TextureFormat.BGRA32:
            case TextureFormat.RGBA32:
                return 32;
            case TextureFormat.RGB24:
                return 24;
            case TextureFormat.R16:
                return 16;
            case TextureFormat.R8:
            case TextureFormat.Alpha8:
                return 8;
            default:
                throw new ArgumentException("unsupported format {0}", format.ToString());

        }
    }

    public RsFrameProvider Source;

    [System.Serializable]
    public class TextureEvent : UnityEvent<Texture> { }

    public Stream _stream;
    public Format _format;
    public int _streamIndex;

    public FilterMode filterMode = FilterMode.Point;

    protected Texture2D texture;


    [Space]
    public TextureEvent textureBinding;

    FrameQueue q;
    Predicate<Frame> matcher;

    float TableThreshold = 50f;
    bool once = true;

    [Range (0,1)]
    public float redMin = 0.0f;

    [Range(0, 1)]
    public float redMax = 0.0f;

    [Range(0, 1)]
    public float greenMin = 0.0f;

    [Range(0, 1)]
    public float greenMax = 0.4f;

    [Range(0, 1)]
    public float blueMin = 0.0f;

    [Range(0, 1)]
    public float blueMax = 1f;

    public bool useFilter = true;

    void Start()
    {
        Source.OnStart += OnStartStreaming;
        Source.OnStop += OnStopStreaming;
    }

    void OnDestroy()
    {
        if (texture != null)
        {
            Destroy(texture);
            texture = null;
        }

        if (q != null)
        {
            q.Dispose();
        }
    }

    protected void OnStopStreaming()
    {
        Source.OnNewSample -= OnNewSample;
        if (q != null)
        {
            q.Dispose();
            q = null;
        }
    }

    public void OnStartStreaming(PipelineProfile activeProfile)
    {
        q = new FrameQueue(1);
        matcher = new Predicate<Frame>(Matches);
        Source.OnNewSample += OnNewSample;
    }

    private bool Matches(Frame f)
    {
        using (var p = f.Profile)
            return p.Stream == _stream && p.Format == _format && p.Index == _streamIndex;
    }

    void OnNewSample(Frame frame)
    {
        try
        {
            if (frame.IsComposite)
            {
                using (var fs = frame.As<FrameSet>())
                using (var f = fs.FirstOrDefault(matcher))
                {
                    if (f != null)
                        q.Enqueue(f);
                    return;
                }
            }

            if (!matcher(frame))
                return;

            using (frame)
            {
                q.Enqueue(frame);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            // throw;
        }

    }

    bool HasTextureConflict(VideoFrame vf)
    {
        return !texture ||
            texture.width != vf.Width ||
            texture.height != vf.Height ||
            BPP(texture.format) != vf.BitsPerPixel;
    }

    protected void LateUpdate()
    {
        if (q != null)
        {
            VideoFrame frame;
            if (q.PollForFrame<VideoFrame>(out frame))
                using (frame)
                    ProcessFrame(frame);
        }
    }

    private void ProcessFrame(VideoFrame frame)
    {
        if (HasTextureConflict(frame))
        {
            if (texture != null)
            {
                Destroy(texture);
            }

            using (var p = frame.Profile) {
                bool linear = (QualitySettings.activeColorSpace != ColorSpace.Linear)
                    || (p.Stream != Stream.Color && p.Stream != Stream.Infrared);
                texture = new Texture2D(frame.Width, frame.Height, Convert(p.Format), false, linear)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = filterMode
                };

               

            }

            textureBinding.Invoke(texture);
        }


        texture.LoadRawTextureData(frame.Data, frame.Stride * frame.Height);

        if (useFilter)
        {
            var colors = texture.GetPixels();
            Color[,] rawImage2D = new Color[texture.height, texture.width];

            for (int i = 0; i < colors.Length; i++)
            {

                int row = (int)Math.Floor(i / (float)texture.width);
                int col = i - (row * texture.width);

                rawImage2D[row, col] = colors[i];

               
            }

            
            Color[,] aArray = new Color[texture.height, texture.width];

            for (int i = 0; i < colors.Length; i++)
            {

                int row = (int)Math.Floor(i / (float)texture.width);
                int col = i - (row * texture.width);

                aArray[row, col] = colors[i];


            }
            


            Color[] filteredColors = new Color[colors.Length];
            int index = 0;
            //print 2d color array only once
            for (int i = 0; i < texture.height; i++)
            {
                for (int j = 0; j < texture.width; j++)
                {
                    if ((rawImage2D[i, j].r >= redMin) && (rawImage2D[i, j].g >= greenMin) && (rawImage2D[i, j].b >= blueMin) &&
                      (rawImage2D[i, j].r <= redMax) && (rawImage2D[i, j].g <= greenMax) && (rawImage2D[i, j].b <= blueMax))
                    ////if ((rawImage2D[i, j].r > redMin) && (rawImage2D[i, j].r < redMax))
                    ////{
                    //if (rawImage2D[i,j].r >= redMin)
                    {
                        filteredColors[index] = rawImage2D[i, j];
                        aArray[i, j] = rawImage2D[i, j];
                    }
                    else
                    {
                        filteredColors[index] = new Color(0, 0, 0);
                        aArray[i, j] = new Color(0, 0, 0);
                    }
                    index++;
                }
            }


            if (once == true)
            {

                //System.IO.File.WriteAllLines("WriteLines.txt",
                //rawImage2D.Select(ri => (double.Parse(ri.Text)).ToString()));

                //System.IO.File.WriteAllLines(@"C:\Users\Public\TestFolder\WriteLines.txt", );

                

                System.IO.StreamWriter streamWriter = new System.IO.StreamWriter("bird.txt");
                string output = "";
                for (int g = 0; g < texture.height; g++)
                {
                    for (int j = 0; j < texture.width; j++)
                    {
                        output += aArray[g,j].ToString();
                    }
                    streamWriter.WriteLine(output);
                    output = "";
                }
                streamWriter.Close();


                once = false;

                /*

                int k = 0;
                System.IO.StreamWriter streamWriter = new System.IO.StreamWriter("bird.txt");
                string output = "";
                for (int g = 0; g < filteredColors.GetUpperBound(0); g++)
                {
                    
                    //for (int j = 0; j < aArray.GetUpperBound(1); j++)
                    //{
                    output += filteredColors[g].ToString();
                    //}
                    streamWriter.Write(output);
                    output = "";

                    if (k >= 640)
                    {
                        streamWriter.WriteLine("");
                    }
                    k++;
                        
                }
                    streamWriter.Close();


                    once = false;
                  */  
            }
            

            texture.SetPixels(filteredColors);

            

            int [,] a = new int[585,700];
            for (int w = 1; w < 480; w++)
            {
                for (int q = 1; q < 640; q++)
                {
                    if((aArray[w, q].g >= greenMin) && (aArray[w, q].b >= blueMin) &&
                      (aArray[w, q].g <= greenMax) && (aArray[w, q].b <= blueMax)) {

                        if((aArray[w-1, q].g >= greenMin) && (aArray[w-1, q].b <= blueMin) &&
                      (aArray[w-1, q].g >= greenMax) && (aArray[w-1, q].b >= blueMax))
                        {
                            aArray[w-1, q] = new Color(1, 0, 0);
                        }
                    }
                }
            }
            
        }
        texture.Apply();
    }
}
