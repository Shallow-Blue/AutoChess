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

            using (var p = frame.Profile) {
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
            Color[,] aArray = new Color[texture.height, texture.width];
            //Color[,] arrayShape = new Color[texture.height, texture.width];
            //Color[,] boxArray = new Color[texture.height, texture.width];
            //Color[,] boxCenter = new Color[texture.height, texture.width];

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
                for (int i = 60; i < 480; i= i + 60)
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
                int row = 1;
                int col = 1;
                int sum = 0;
                float totalHeight = 0f;
                float avrageHeight = 0f;
                float maxHeight = 0.5f;
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
                                //cdFrame.pixels[y, x].color = new Color(1, 0, 0, 1);
                            }

                        }

                    }
                    avrageHeight = totalHeight / sum;
                    //classification using total number of pixels, maximum height, average height

                    //[1,1] 
                    if (row == 1 && col == 1)
                    {
                        if (sum > 500f && sum < 900f && avrageHeight > 0.415f && avrageHeight < 0.43f && maxHeight > 0.4f && maxHeight < 0.41f)
                        {
                            pawnText = true;
                            //initializing the prefabs and giving them the position of the pieces placed on the board
                            var go = GameObject.Instantiate(Pawn);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();

                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                        if (sum > 2900f && sum < 3400f && avrageHeight > 0.414f && avrageHeight < 0.419f && maxHeight > 0.390f && maxHeight < 0.395f)
                        {
                            rookText = true;
                            var go = GameObject.Instantiate(Rook);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                        if (sum > 840 && sum < 1000 && avrageHeight > 0.405 && avrageHeight < 0.415 && maxHeight > 0.385 && maxHeight < 0.395)
                        {
                            //bishop
                            var go = GameObject.Instantiate(Bishop);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }
                        if (sum > 1200 && sum < 1400 && avrageHeight > 0.405 && avrageHeight < 0.415 && maxHeight > 0.389 && maxHeight < 0.399)
                        {
                            horseText = true;
                            var go = GameObject.Instantiate(Knight);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                        if (sum > 1000 && sum < 1550 && avrageHeight > 0.401 && avrageHeight < 0.409 && maxHeight > 0.384 && maxHeight < 0.389)
                        {
                            queenText = true;
                            var go = GameObject.Instantiate(Queen);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }
                        if (sum > 1400 && sum < 1530 && avrageHeight > 0.398 && avrageHeight < 0.405 && maxHeight > 0.379 & maxHeight < 0.384)
                        {
                            kingText = true;
                            var go = GameObject.Instantiate(King);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }
                    }
                    //[2,1]
                    else if (row == 2 && col == 1)
                    {
                        if (sum > 2500 && sum < 3400 && avrageHeight > 0.410 && avrageHeight < 0.42 && maxHeight > 0.380 && maxHeight < 0.389)
                        {
                            horseText = true;
                            var go = GameObject.Instantiate(Knight);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[3,1]
                    else if (row == 3 && col == 1)
                    {
                        if (sum > 1000 && sum < 3000 && avrageHeight > 0.410 && avrageHeight < 0.419 && maxHeight > 0.380 && maxHeight < 0.389)
                        {
                            //bishop
                            var go = GameObject.Instantiate(Bishop);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[4,1]
                    else if (row == 4 && col == 1)
                    {
                        if (sum > 1000 && sum < 2000 && avrageHeight > 0.400 && avrageHeight < 0.419 && maxHeight > 0.370 & maxHeight < 0.389)
                        {
                            kingText = true;
                            var go = GameObject.Instantiate(King);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[5,1]
                    else if (row == 5 && col == 1)
                    {
                        if (sum > 1000 && sum < 1700 && avrageHeight > 0.401 && avrageHeight < 0.409 && maxHeight > 0.380 && maxHeight < 0.389)
                        {
                            queenText = true;
                            var go = GameObject.Instantiate(Queen);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[6,1]
                    else if (row == 6 && col == 1)
                    {
                        if (sum > 600 && sum < 2000 && avrageHeight > 0.400 && avrageHeight < 0.419 && maxHeight > 0.380 && maxHeight < 0.39)
                        {
                            //bishop
                            var go = GameObject.Instantiate(Bishop);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[7,1]
                    else if (row == 7 && col == 1)
                    {
                        if (sum > 1000 && sum < 1600 && avrageHeight > 0.410 && avrageHeight < 0.419 && maxHeight > 0.390 && maxHeight < 0.399)
                        {
                            horseText = true;
                            var go = GameObject.Instantiate(Knight);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[8,1]
                    else if (row == 8 && col == 1)
                    {
                        if (sum > 700f && sum < 1200f && avrageHeight > 0.410f && avrageHeight < 0.419f && maxHeight > 0.399f && maxHeight < 0.409f)
                        {
                            rookText = true;
                            var go = GameObject.Instantiate(Rook);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[1,2] [2,2] [3,2] 
                    if (row > 0 && row < 4 && col == 2)
                    {
                        if (sum > 800f && sum < 3400f && avrageHeight > 0.410f && avrageHeight < 0.425f && maxHeight > 0.390f && maxHeight < 0.409f)
                        {
                            pawnText = true;
                            //initializing the prefabs and giving them the position of the pieces placed on the board
                            var go = GameObject.Instantiate(Pawn);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();

                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[4,2] [5,2] [6,2] [7,2]
                    if (row > 3 && row < 8 && col == 2)
                    {
                        if (sum > 400f && sum < 1000f && avrageHeight > 0.409f && avrageHeight < 0.425f && maxHeight > 0.396f && maxHeight < 0.41f)
                        {
                            pawnText = true;
                            //initializing the prefabs and giving them the position of the pieces placed on the board
                            var go = GameObject.Instantiate(Pawn);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();

                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[1,2]
                    if (row == 1 && col == 2)
                    {
                        if (sum > 2400f && sum < 3000f && avrageHeight > 0.410f && avrageHeight < 0.419f && maxHeight > 0.390f && maxHeight < 0.399f)
                        {
                            rookText = true;
                            var go = GameObject.Instantiate(Rook);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[2,2]
                    if (row == 2 && col == 2)
                    {
                        if (sum > 2000 && sum < 2500 && avrageHeight > 0.410 && avrageHeight < 0.419 && maxHeight > 0.380 && maxHeight < 0.389)
                        {
                            horseText = true;
                            var go = GameObject.Instantiate(Knight);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[3,2]
                    if (row == 3 && col == 2)
                    {
                        if (sum > 1200 && sum < 1500 && avrageHeight > 0.410 && avrageHeight < 0.419 && maxHeight > 0.380 && maxHeight < 0.389)
                        {
                            //bishop
                            var go = GameObject.Instantiate(Bishop);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[4,2]
                    if (row == 4 && col == 2)
                    {
                        if (sum > 1200 && sum < 1400 && avrageHeight > 0.400 && avrageHeight < 0.409 && maxHeight > 0.370 & maxHeight < 0.379)
                        {
                            kingText = true;
                            var go = GameObject.Instantiate(King);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[5,2]
                    if (row == 5 && col == 2)
                    {
                        if (sum > 800 && sum < 1200 && avrageHeight > 0.400 && avrageHeight < 0.409 && maxHeight > 0.380 && maxHeight < 0.389)
                        {
                            queenText = true;
                            var go = GameObject.Instantiate(Queen);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[6,2]
                    if (row == 6 && col == 2)
                    {
                        if (sum > 500 && sum < 900 && avrageHeight > 0.400 && avrageHeight < 0.419 && maxHeight > 0.390 && maxHeight < 0.399)
                        {
                            //bishop
                            var go = GameObject.Instantiate(Bishop);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[7,2]
                    if (row == 7 && col == 2)
                    {
                        if (sum > 1200 && sum < 1500 && avrageHeight > 0.400 && avrageHeight < 0.419 && maxHeight > 0.390 && maxHeight < 0.399)
                        {
                            horseText = true;
                            var go = GameObject.Instantiate(Knight);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //[8,2]
                    if (row == 8 && col == 2)

                    {
                        if (sum > 300f && sum < 800f && avrageHeight > 0.410f && avrageHeight < 0.419f && maxHeight > 0.400f && maxHeight < 0.409f)
                        {
                            pawnText = true;
                            //initializing the prefabs and giving them the position of the pieces placed on the board
                            var go = GameObject.Instantiate(Pawn);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();

                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }
                        if (sum > 800f && sum < 1200f && avrageHeight > 0.410f && avrageHeight < 0.419f && maxHeight > 0.400f && maxHeight < 0.409f)
                        {
                            rookText = true;
                            var go = GameObject.Instantiate(Rook);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();
                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }
                    //whole col 3
                    if (col== 3)
                    {
                        if (sum > 300f && sum < 2100f && avrageHeight > 0.410f && avrageHeight < 0.429f && maxHeight > 0.390f && maxHeight < 0.41f)
                        {
                            pawnText = true;
                            //initializing the prefabs and giving them the position of the pieces placed on the board
                            var go = GameObject.Instantiate(Pawn);
                            var PawnScript = go.GetComponent<chess3d.Pawn>();

                            go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                        }

                    }

                    /*
                    
                    if (sum> 650 && sum < 720 && maxHeight>0.4 && maxHeight<0.41 )
                    {
                        pawnText = true;
                        //initializing the prefabs and giving them the position of the pieces placed on the board
                        var go = GameObject.Instantiate(Pawn);
                        var PawnScript = go.GetComponent<chess3d.Pawn>();
                        
                        go.transform.position = new Vector3(StartX + ((col -1 ) * 45), StartY - ((row -1 ) * 45), StartZ);
                    }

                    if (sum>1300 && sum<1450 && avrageHeight>0.40 && avrageHeight < 0.42)
                    {
                        horseText = true;
                        var go = GameObject.Instantiate(Knight);
                        var PawnScript = go.GetComponent<chess3d.Pawn>();
                        
                        go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                    }

                    if (sum>1000f && sum<1250f && avrageHeight>0.405f && avrageHeight < 0.42f && maxHeight>0.390f && maxHeight<0.410f)
                    {
                        rookText = true;
                        var go = GameObject.Instantiate(Rook);
                        var PawnScript = go.GetComponent<chess3d.Pawn>();
                        
                        go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                    }

                    if (sum > 860 && sum < 940 && avrageHeight > 0.40 && avrageHeight < 0.42)
                    {
                        //bishop
                        var go = GameObject.Instantiate(Bishop);
                        var PawnScript = go.GetComponent<chess3d.Pawn>();
                        
                        go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                    }

                    if (sum > 1300 && sum < 1400 && avrageHeight > 0.39 && avrageHeight < 0.41)
                    {
                        queenText = true;
                        var go = GameObject.Instantiate(Queen);
                        var PawnScript = go.GetComponent<chess3d.Pawn>();
                        
                        go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                    }

                    if (sum > 1400 && sum < 1530 && avrageHeight > 0.39 && avrageHeight < 0.41)
                    {
                        kingText = true;
                        var go = GameObject.Instantiate(King);
                        var PawnScript = go.GetComponent<chess3d.Pawn>();

                        go.transform.position = new Vector3(StartX + ((col - 1) * 45), StartY - ((row - 1) * 45), StartZ);
                    }
                    */

                    //printing the data for squares [1,1], [5,5] and [8,8] to see how the pixels vary for the same piece in different parts of the board
                    if (row ==3 && col==1)
                    {
                        Debug.Log("row: " + row + "column: " + col);
                        Debug.Log("sum: " + sum);
                        Debug.Log("avrageHeight: " + avrageHeight);
                        Debug.Log("maxHeight: " + maxHeight);
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
                    
                        if(width > 25 && width < 50 && cdFrame.pixels[i, j].depth < ground)
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
            //CovertToOutputPixelsUnityTexture(true);
            //List<Rect> list = new List<Rect>();
            //Rect r = new Rect(0, 0, 50, 50);
            //list.Add(r);

           // Debug.Log("candidates: " + list.Count);

            //if (list.Count < 10)
            //{
             //   foreach (var c in list)
              //  {
               //     var go = GameObject.Instantiate(bbPrefab);
                //    var bbScript = go.GetComponent<chess3d.BoundingBox>();
                 //   go.transform.position = c.center;
                  // bbScript.width = c.width;
                   // bbScript.height = c.height;

                //}
           // }
            //foreach (var l in list)
            //{
             //   var center = l.center;
                //var go = GameObject.Instantiate(bbPrefab);
                //var bbScript = go.GetComponent<chess3d.BoundingBox>();

           // }


            //if (boundingBox)
            //{
            //    bool firstPixel = true;

            //    for (int w = 1; w < texture.height - 1; w++)
            //    {
            //        for (int q = 1; q < texture.width - 1; q++)
            //        {

            //            if (aArray[w, q].r == 1 && firstPixel)
            //            {
            //                boxArray[w, q] = aArray[w, q];
            //                firstPixel = false;
            //            }
            //            if (aArray[w, q].r == 0 && aArray[w, q].b == 0 && aArray[w, q].g == 0)
            //            {
            //                firstPixel = true;
            //            }
            //        }
            //    }
            //}

            //if (boundingBoxMid)
            //{
            //    for (int w = 1; w < texture.height - 1; w++)
            //    {
            //        for (int q = 1; q < texture.width - 1; q++)
            //        {
            //            if (boxArray[w, q].r == 1)
            //            {
            //                if ((w + 8 < texture.height) && (q + 8 < texture.width))
            //                {
            //                    boxCenter[w, q] = boxArray[w + 8, q + 8];
            //                    var go = GameObject.Instantiate(bbPrefab);
            //                    var bbScript = go.GetComponent<chess3d.BoundingBox>();
            //                    bbScript.width = 0.3f;
            //                    bbScript.height = 0.3f;

            //                    float x = w / 100, y = q / 100, z = 0.3f; //change this!

            //                    go.transform.position = new Vector3(x, y, z);
            //                }
            //            }
            //        }
            //    }
            //}

            //if (once == true)
            //{
            //    System.IO.StreamWriter streamWriter = new System.IO.StreamWriter("bird.txt");
            //    string output = "";
            //    for (int g = 0; g < texture.height; g++)
            //    {
            //        for (int j = 0; j < texture.width; j++)
            //        {
            //            output += boxArray[g, j].ToString();
            //        }
            //        streamWriter.WriteLine(output);
            //        output = "";
            //    }
            //    streamWriter.Close();


            //    once = false;
            //}
            ///*
            //            if (countRed)
            //            {
            //                int red = 0;
            //                for (int w = 170; w < texture.height - 170; w++)
            //                {
            //                    for (int q = 220; q < texture.width - 220; q++)
            //                    {
            //                        if (aArray[w, q].r == 1)
            //                        {
            //                            red++;
            //                        }
            //                    }
            //                }
            //                Debug.Log(red);
            //            }

            //  */

            ///*
            //Color[,] arrayPawn = new Color[texture.height, texture.width];
            //if (shape)
            //{
            //    for (int w = 170; w < texture.height - 170; w++)
            //    {
            //        for (int q = 220; q < texture.width - 220; q++)
            //        {
            //            if (aArray[w, q].r == 1)
            //            {
            //                arrayPawn[w, q] = new Color(1, 0, 0, 1);


            //            }
            //        }
            //    }
            //}
            //*/

            //Color[] filteredPiece = new Color[colors.Length];
            //int index1 = 0;
            ////print 2d color array only once
            //for (int i = 0; i < texture.height; i++)
            //{
            //    for (int j = 0; j < texture.width; j++)
            //    {

            //        filteredPiece[index1] = aArray[i, j];
            //        index1++;
            //    }
            //}
            ///*
            //Color[] Pawn = new Color[colors.Length];
            //int indexPawn = 0;
            ////print 2d color array only once
            //for (int i = 0; i < texture.height; i++)
            //{
            //    for (int j = 0; j < texture.width; j++)
            //    {

            //        Pawn[indexPawn] = arrayPawn[i, j];
            //        indexPawn++;
            //    }
            //}
            //*/

            //texture.SetPixels(CountourArray);
            //CovertToOutputPixelsUnityTexture(isColor);
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
        if ( bpp== 24) //its color
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


public class ColorDepth {

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

    public ColorDepthFrame() {

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

 