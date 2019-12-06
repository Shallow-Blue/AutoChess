using Intel.RealSense;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


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

    //float TableThreshold = 50f;
    public bool once = false;

    public bool boundingBox = false;

    public bool boundingBoxMid = false;

    [Range(0, 1)]
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

    public bool useContour = false;

    public bool shape = false;

    public bool pawnText = false;

    public bool horseText = false;

    public bool rookText = false;

    public bool kingText = false;

    public bool queenText = false;

    public bool countRed = false;

    public GameObject bbPrefab;

    public GameObject Pawn;

    public GameObject Rook;

    public GameObject Knight;

    public GameObject Bishop;

    public GameObject Queen;

    public GameObject King;

    private bool isColor = false;
    private bool isDepth = false;

    static ColorDepthFrame cdFrame;

    public bool Tshape = false;

    public bool helpingLines = false;

    public bool chessboard = false;

    [Range(0, 1)]
    public float ground = 0.40f;


    //Debugging the candidate list
    [Range(0, 640)] //width
    public int startX = 0;

    [Range(0, 480)] //height
    public int startY = 0;

    [Range(0, 640)] //width
    public int endX = 640;

    [Range(0, 480)] //height
    public int endY = 480;

    int rounds = 0;
    // Squears variables
    float S11;
    float AH11;
    float MH11;


    void Start()
    {
        Source.OnStart += OnStartStreaming;
        Source.OnStop += OnStopStreaming;

        InitializeCommonDataStructure();

    }

    private void InitializeCommonDataStructure()
    {
        cdFrame = new ColorDepthFrame();
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

            using (var p = frame.Profile)
            {
                bool linear = (QualitySettings.activeColorSpace != ColorSpace.Linear)
                    || (p.Stream != Stream.Color && p.Stream != Stream.Infrared);
                texture = new Texture2D(frame.Width, frame.Height, Convert(p.Format), false, linear)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = filterMode,
                    alphaIsTransparency = true
                };



            }

            textureBinding.Invoke(texture);
        }


        texture.LoadRawTextureData(frame.Data, frame.Stride * frame.Height);

        DetermineDataSourceDepthOrColor(frame.BitsPerPixel);

        //pass 1 (after pass 1, wait for depth pass)
        DoColorPass();

        //pass 2 (after pass 2, immediately do pass 3, there is no waiting)
        DoDepthPass();

        //pass 3
        DoProcessingPass();

        texture.Apply();
    }

    private void DoColorPass()
    {

        //set pixel properties
        if (isColor)
        {
            //set frame properties
            cdFrame.useColorFilter = useFilter;
            cdFrame.colorTexture = texture;
            cdFrame.redMin = redMin;
            cdFrame.redMax = redMax;
            cdFrame.greenMin = greenMin;
            cdFrame.greenMax = greenMax;
            cdFrame.blueMin = blueMin;
            cdFrame.blueMax = blueMax;

            var colors = texture.GetPixels();

            //convert from 1D color array to 2D color array
            //save the color data in the shared colordepth frame
            for (int i = 0; i < colors.Length; i++)
            {

                int row = (int)Math.Floor(i / (float)texture.width);
                int col = i - (row * texture.width);

                cdFrame.pixels[row, col].color = colors[i];
            }

            //save the current (unfiltered) color data to color texture
            CovertToOutputPixelsUnityTexture(isColor);
        }
    }

    private void DoDepthPass()
    {

        //set pixel properties
        if (isDepth)
        {
            //set frame properties
            cdFrame.useDepthFilter = useFilter;
            cdFrame.depthTexture = texture;

            var colors = texture.GetPixels();

            //convert from 1D depth array to 2D depth array
            //save the depth data in the shared colordepth frame
            for (int i = 0; i < colors.Length; i++)
            {

                int row = (int)Math.Floor(i / (float)texture.width);
                int col = i - (row * texture.width);

                var depth = colors[i].r * 65536 * 0.001f;
                cdFrame.pixels[row, col].depth = depth;
            }

            //save the current (unfiltered) depth data to depth texture
            CovertToOutputPixelsUnityTexture(isColor);
        }
    }

    private void DoProcessingPass()
    {
        if (isDepth)
        {
            //PV = pieceValue
            float[] PV = new float[64 * 3];
            Color[,] aArray = new Color[texture.height, texture.width];

            if (cdFrame.useColorFilter)
            {
                for (int i = 0; i < texture.height; i++)
                {
                    for (int j = 0; j < texture.width; j++)
                    {
                        if ((cdFrame.pixels[i, j].color.r >= cdFrame.redMin) && (cdFrame.pixels[i, j].color.g >= cdFrame.greenMin) && (cdFrame.pixels[i, j].color.b >= cdFrame.blueMin) &&
                            (cdFrame.pixels[i, j].color.r <= cdFrame.redMax) && (cdFrame.pixels[i, j].color.g <= cdFrame.greenMax) && (cdFrame.pixels[i, j].color.b <= cdFrame.blueMax))
                        {
                            //do nothing
                        }
                        else
                        {
                            //var c = System.Drawing.Color.FromArgb(0x01B5EE);
                            cdFrame.pixels[i, j].color = Color.black;
                        }
                    }
                }

                CovertToOutputPixelsUnityTexture(true);
                //CovertToOutputPixelsUnityTexture(false);
            }

            if (useContour)
            {
                for (int w = 5; w < texture.height - 5; w++)
                {
                    for (int q = 5; q < texture.width - 5; q++)
                    {
                        if (cdFrame.pixels[w, q].depth < ground)
                        {
                            if ((w >= startX) && (w <= endX) && (q >= startY) && (q <= endY))
                            {
                                if (cdFrame.pixels[w - 1, q].depth > ground && cdFrame.pixels[w - 2, q].depth > ground && cdFrame.pixels[w - 3, q].depth > ground && cdFrame.pixels[w - 4, q].depth > ground)
                                {

                                    cdFrame.pixels[w - 1, q].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w - 2, q].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w - 3, q].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w - 4, q].color = new Color(1, 0, 0, 1);
                                }

                                if (cdFrame.pixels[w + 1, q].depth > ground && cdFrame.pixels[w + 2, q].depth > ground && cdFrame.pixels[w + 3, q].depth > ground && cdFrame.pixels[w + 4, q].depth > ground)
                                {
                                    cdFrame.pixels[w + 1, q].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w + 2, q].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w + 3, q].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w + 4, q].color = new Color(1, 0, 0, 1);
                                }


                                if (cdFrame.pixels[w, q - 1].depth > ground && cdFrame.pixels[w, q - 2].depth > ground && cdFrame.pixels[w, q - 3].depth > ground && cdFrame.pixels[w, q - 4].depth > ground)
                                {
                                    cdFrame.pixels[w, q - 1].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w, q - 2].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w, q - 3].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w, q - 4].color = new Color(1, 0, 0, 1);
                                }

                                if (cdFrame.pixels[w, q + 1].depth > ground && cdFrame.pixels[w, q + 2].depth > ground && cdFrame.pixels[w, q + 3].depth > ground && cdFrame.pixels[w, q + 4].depth > ground)
                                {
                                    cdFrame.pixels[w, q + 1].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w, q + 2].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w, q + 3].color = new Color(1, 0, 0, 1);
                                    cdFrame.pixels[w, q + 4].color = new Color(1, 0, 0, 1);
                                }
                            }
                        }
                    }
                }
                CovertToOutputPixelsUnityTexture(true);
            }

            //helping lines that show the visual representation of the chessboard algorithm (squares)
            if (helpingLines)
            {
                for (int i = 60; i < 480; i = i + 60)
                {
                    for (int j = 80; j <= 560; j++)
                    {
                        cdFrame.pixels[i, j].color = new Color(0, 0, 0, 1);
                    }
                }
                for (int i = 0; i < 480; i++)
                {
                    for (int j = 80; j <= 560; j = j + 60)
                    {
                        cdFrame.pixels[i, j].color = new Color(0, 0, 0, 1);
                    }
                }
                CovertToOutputPixelsUnityTexture(true);
            }
            //divided the picture into 60x60 pixels squares
            if (chessboard)
            {
                for (int rounds = 1; rounds < 11; rounds++)
                {


                    int row = 1;
                    int col = 1;
                    int sum = 0;
                    float totalHeight = 0f;
                    float avrageHeight = 0f;
                    float maxHeight = 0.5f;
                    int RV = 0;
                    int i = 0;
                    int j = 80;
                    int StartX = 290, StartY = 571, StartZ = -3;

                    //going through each square and processing all pixels above ground

                    while (i < 480)
                    {
                        for (int y = i; y < 60 + i; y++)
                        {

                            for (int x = j; x < 60 + j; x++)
                            {
                                if (cdFrame.pixels[y, x].depth < ground && j < 640 && cdFrame.pixels[y, x].depth != 0)
                                {
                                    sum++;
                                    if (cdFrame.pixels[y, x].depth != 0)
                                    {

                                        totalHeight = cdFrame.pixels[y, x].depth + totalHeight;
                                    }

                                    if (maxHeight > cdFrame.pixels[y, x].depth && cdFrame.pixels[y, x].depth != 0)
                                    {

                                        maxHeight = cdFrame.pixels[y, x].depth;
                                    }

                                }

                            }

                        }
                        avrageHeight = totalHeight / sum;
                        //classification using total number of pixels, maximum height, average height

                        //[1,1] 
                        if (row == 1 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }

                        }

                        //[1,2]
                        else if (row == 1 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[1,3]
                        else if (row == 1 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[1,4]
                        else if (row == 1 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[1,5]
                        else if (row == 1 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[1,6]
                        else if (row == 1 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[1,7]
                        else if (row == 1 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[1,8]
                        else if (row == 1 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,1]
                        else if (row == 2 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,2]
                        else if (row == 2 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,3]
                        else if (row == 2 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,4]
                        else if (row == 2 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,5]
                        else if (row == 2 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,6]
                        else if (row == 2 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,7]
                        else if (row == 2 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[2,8]
                        else if (row == 2 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,1]
                        else if (row == 3 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,2]
                        else if (row == 3 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,3]
                        else if (row == 3 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,4]
                        else if (row == 3 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,5]
                        else if (row == 3 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,6]
                        else if (row == 3 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,7]
                        else if (row == 3 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[3,8]
                        else if (row == 3 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,1]
                        else if (row == 4 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,2]
                        else if (row == 4 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,3]
                        else if (row == 4 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,4]
                        else if (row == 4 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,5]
                        else if (row == 4 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,6]
                        else if (row == 4 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,7]
                        else if (row == 4 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[4,8]
                        else if (row == 4 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,1]
                        else if (row == 5 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,2]
                        else if (row == 5 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,3]
                        else if (row == 5 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,4]
                        else if (row == 5 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,5]
                        else if (row == 5 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,6]
                        else if (row == 5 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,7]
                        else if (row == 5 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[5,8]
                        else if (row == 5 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,1]
                        else if (row == 6 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,2]
                        else if (row == 6 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,3]
                        else if (row == 6 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,4]
                        else if (row == 6 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,5]
                        else if (row == 6 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,6]
                        else if (row == 6 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,7]
                        else if (row == 6 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[6,8]
                        else if (row == 6 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,1]
                        else if (row == 7 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,2]
                        else if (row == 7 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,3]
                        else if (row == 7 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,4]
                        else if (row == 7 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,5]
                        else if (row == 7 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,6]
                        else if (row == 7 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,7]
                        else if (row == 7 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[7,8]
                        else if (row == 7 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,1]
                        else if (row == 8 && col == 1)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,2]
                        else if (row == 8 && col == 2)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,3]
                        else if (row == 8 && col == 3)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,4]
                        else if (row == 8 && col == 4)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,5]
                        else if (row == 8 && col == 5)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,6]
                        else if (row == 8 && col == 6)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (PV[RV - 2] > 484.045f && PV[RV - 2] < 884.045f && PV[RV - 1] > 0.451587793827057f && PV[RV - 1] < 0.456587793827057f && PV[RV] > 3163.94720703125f && PV[RV] < 3163.95220703125f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,7]
                        else if (row == 8 && col == 7)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 3105f && PV[RV - 2] < 3505f && PV[RV - 1] > 0.403873918056488f && PV[RV - 1] < 0.408873918056488f && PV[RV] > 0.375505772829056f && PV[RV] < 0.380505772829056f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (PV[RV - 2] > 467.895f && PV[RV - 2] < 867.895f && PV[RV - 1] > 0.443136808872223f && PV[RV - 1] < 0.448136808872223f && PV[RV] > 3128.8168359375f && PV[RV] < 3128.8218359375f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }

                        //[8,8]
                        else if (row == 8 && col == 8)
                        {
                            PV[RV] = PV[RV] + sum;
                            RV++;

                            PV[RV] = PV[RV] + avrageHeight;
                            RV++;

                            PV[RV] = PV[RV] + maxHeight;

                            if (rounds == 10)
                            {
                                PV[RV - 2] = PV[RV - 2] / rounds;

                                PV[RV - 1] = PV[RV - 1] / rounds;

                                PV[RV] = PV[RV] / rounds;
                                Debug.Log("Row: " + row + "Col: " + col);
                                Debug.Log("PV[RV - 2] > " + (PV[RV - 2] - 200) + "f && PV[RV - 2] < " + (PV[RV - 2] + 200) + "f " +
                                    "&& PV[RV - 1] > " + (PV[RV - 1] - 0.0025) + "f && PV[RV -1 ] < " + (PV[RV - 1] + 0.0025) + "f" +
                                    " && PV[RV] > " + (PV[RV] - 0.0025) + "f && PV[RV] < " + (PV[RV] + 0.0025) + "f");


                                //pawn
                                if (S11 > 3278f && S11 < 3678f && AH11 > 0.421067235469818f && AH11 < 0.426067235469818f && MH11 > 0.391506043672562f && MH11 < 0.396506043672562f)
                                {
                                    //initializing the prefabs and giving them the position of the pieces placed on the board
                                    var go = GameObject.Instantiate(Pawn);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //rook
                                if (PV[RV - 2] > 460.4819f && PV[RV - 2] < 860.4819f && PV[RV - 1] > 0.432627556324005f && PV[RV - 1] < 0.437627556324005f && PV[RV] > 0.409506318569183f && PV[RV] < 0.414506318569183f)
                                {
                                    var go = GameObject.Instantiate(Rook);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //bishop
                                if (S11 > 3159f && S11 < 3559f && AH11 > 0.416315553188324f && AH11 < 0.421315553188324f && MH11 > 0.377505806684494f && MH11 < 0.382505806684494f)
                                {
                                    var go = GameObject.Instantiate(Bishop);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //knight
                                if (S11 > 3246f && S11 < 3646f && AH11 > 0.414459524154663f && AH11 < 0.419459524154663f && MH11 > 0.381505844593048f && MH11 < 0.386505844593048f)
                                {
                                    var go = GameObject.Instantiate(Knight);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                                //Queen
                                if (S11 > 3109f && S11 < 3509f && AH11 > 0.412302491664886f && AH11 < 0.417302491664886f && MH11 > 0.374505785703659f && MH11 < 0.379505785703659f)
                                {
                                    var go = GameObject.Instantiate(Queen);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }

                                //king
                                if (S11 > 3192f && S11 < 3592f && AH11 > 0.4098215675354f && AH11 < 0.4148215675354f && MH11 > 0.368505713939667f && MH11 < 0.373505713939667f)
                                {
                                    var go = GameObject.Instantiate(King);
                                    var PawnScript = go.GetComponent<chess3d.Pawn>();
                                    go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                                }
                            }


                        }


                        //reseting the values after completing one square
                        j = j + 60;
                        sum = 0;
                        totalHeight = 0;
                        avrageHeight = 0;
                        maxHeight = 0.5f;
                        col++;
                        //going to the next row
                        if (j == 560)
                        {
                            j = 80;
                            i = i + 60;
                            col = 1;
                            row++;

                        }
                    }

                    CovertToOutputPixelsUnityTexture(true);
                    // chessboard = false;
                }
            }



            if (Tshape)
            {
                List<Rect> list = new List<Rect>();
                //Rect r = new Rect(i, j, width, height);
                int height = 0;
                int width = 0;
                //bool FirstCandidate = true;
                for (int i = 0; i < texture.height; i++)
                {

                    for (int j = 0; j < texture.width; j++)
                    {
                        if (cdFrame.pixels[i, j].depth < ground)
                        {
                            width++;
                        }

                        if (width > 25 && width < 50 && cdFrame.pixels[i, j].depth < ground)
                        {

                            int mid = width / 2;
                            for (int h = i; h < i + 50; h++)
                            {
                                if (h < texture.height)
                                {
                                    if (cdFrame.pixels[h, j].depth < ground)
                                    {
                                        height++;
                                    }

                                    else
                                    {
                                        if (height > 25 && height < 50)
                                        {
                                            bool isFound = false;
                                            if (list.Count == 0)
                                                isFound = true;
                                            else
                                            {
                                                foreach (var candidate in list)
                                                {
                                                    if ((i >= candidate.x) && (i <= (candidate.x + candidate.width)))
                                                    {

                                                    }
                                                    else
                                                    {

                                                        if ((i >= startX) && (i <= endX) && (j >= startY) && (j <= endY))  //redundant , purely for visual filter
                                                            isFound = true;
                                                    }
                                                }
                                            }

                                            if (isFound)
                                            {
                                                //List<Rect> list = new List<Rect>();
                                                Rect r = new Rect(i, j - width, width, height);
                                                list.Add(r);

                                                //color it in display
                                                for (int row = i; row < (i + height); row++)
                                                {
                                                    for (int col = j; col < (j + width); col++)
                                                    {
                                                        if ((row < 480) && (col < 640)) //hack, we should swap row and col everywhere incl. in cdFrame 
                                                            cdFrame.pixels[row, col].color = Color.yellow;
                                                    }
                                                }


                                            }
                                            height = 0;
                                            width = 0;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        //if (width < 3 && cdFrame.pixels[i, j].depth > ground)
                        //{
                        //    width = 0;
                        //}

                    }

                }
            }

        }
    }
    private void CovertToOutputPixelsUnityTexture(bool isColor)
    {
        Color[] filteredPiece = new Color[307200]; //640*480
        int index = 0;
        if (isColor)
        {

            //print 2d color array only once
            for (int i = 0; i < texture.height; i++)
            {
                for (int j = 0; j < texture.width; j++)
                {
                    filteredPiece[index] = cdFrame.pixels[i, j].color;
                    index++;
                }
            }

            cdFrame.colorTexture.SetPixels(filteredPiece);
            cdFrame.colorTexture.Apply(false);
        }
        else //depth
        {
            //print 2d color array only once
            for (int i = 0; i < texture.height; i++)
            {
                for (int j = 0; j < texture.width; j++)
                {
                    var r = cdFrame.pixels[i, j].depth / (65536 * 0.001f);
                    filteredPiece[index] = new Color(r, 1, 1, 1);
                    index++;
                }
            }

            cdFrame.depthTexture.SetPixels(filteredPiece);
            cdFrame.depthTexture.Apply(false);
        }


    }


    private void DetermineDataSourceDepthOrColor(int bpp)
    {
        if (bpp == 24) //its color
        {
            isColor = true;
            isDepth = false;


        }
        else //its depth
        {
            isColor = false;
            isDepth = true;
        }
    }
}


public class ColorDepth
{

    public Color color { get; set; }
    public float depth { get; set; }
}

public class ColorDepthFrame
{
    public ColorDepth[,] pixels;
    public ColorDepth[,] CandidateList;

    public bool useColorFilter { get; set; }

    public bool useDepthFilter { get; set; }

    public Texture2D colorTexture { get; set; }

    public Texture2D depthTexture { get; set; }

    public float redMax { get; set; }

    public float redMin { get; set; }

    public float greenMin { get; set; }

    public float greenMax { get; set; }

    public float blueMin { get; set; }

    public float blueMax { get; set; }

    public ColorDepthFrame()
    {

        pixels = new ColorDepth[480, 640];

        //initialize
        for (int i = 0; i < 480; i++)
        {
            for (int j = 0; j < 640; j++)
            {
                pixels[i, j] = new ColorDepth();
            }
        }
    }

}

