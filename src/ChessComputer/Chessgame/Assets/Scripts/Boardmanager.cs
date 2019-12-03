using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Boardmanager : MonoBehaviour
{
    public static Boardmanager Instance { set; get; }
    private bool[,] allowedMoves { set; get; }

    public Chessman[,] Chessmans { set; get; }
    private Chessman selectedChessman;

    private const float TILE_SIZE = 1.0f;
    private const float TILE_OFFSET = 0.5F;

    private int selectionX = -1;
    private int selectionY = -1;

    public List<GameObject> chessmanPrefabs;
    private List<GameObject> activeChessman;

    public int[] enPassantMove { set; get; }

    private Quaternion orientation = Quaternion.Euler(0, 180, 0);

    public bool isWhiteTurn = true;
    //private Quaternion blackRot = Quaternion.Euler(0, 180, 0);
    //private Quaternion whiteRot = Quaternion.identity;
    public SerialController serialController;
    public GameObject WhiteTurn;
    public GameObject BlackTurn;
    /*
    public Text BlackTimerText;
    public Text WhiteTimerText;
    private float startBlackTime;
    private float startWhiteTime;
    */
    float currentBlackTime;
    float currentWhiteTime;
    float startingTime = 120.0f;
    [SerializeField] Text CountdownBlack;
    [SerializeField] Text CountdownWhite;

    public InputField TimeValue;

    int firstY;
    int firstX;

    private void Start()
    {
        Instance = this;
        SpawnAllChessmans();
        serialController = GameObject.Find("SerialController").GetComponent<SerialController>();

        currentWhiteTime = startingTime;
        currentBlackTime = startingTime;
    }

    private void Update()
    {
        UpdateSelection();
        DrawChessboard();

        if (isWhiteTurn)
        {

            WhiteTurn.gameObject.SetActive(true);
            BlackTurn.gameObject.SetActive(false);

            currentWhiteTime -= 1 * Time.deltaTime;
            CountdownWhite.text = currentWhiteTime.ToString("f2");

            if(currentWhiteTime <= 0)
            {
                serialController.SendSerialMessage("AT+WWIN");
                Debug.Log("White Wins");
                EndGame();
                return;
            }
            //float tw = Time.time - startWhiteTime;
            //string minutesWhite = ((int)tw / 60).ToString();
            //string secoundsWhite = (tw % 60).ToString("f2");
            //WhiteTimerText.text = minutesWhite + ":" + secoundsWhite;

        }
        else
        {

            WhiteTurn.gameObject.SetActive(false);
            BlackTurn.gameObject.SetActive(true);

            currentBlackTime -= 1 * Time.deltaTime;
            CountdownBlack.text = currentBlackTime.ToString("f2");

            if(currentBlackTime <= 0)
            {
                serialController.SendSerialMessage("AT + BWIN");
                Debug.Log("Black Wins");
                EndGame();
                return;
            }
            //float tb = Time.time - startBlackTime;
            //string minutesBlack = ((int)tb / 60).ToString();
            //string secoundsBlack = (tb % 60).ToString("f2");
            //BlackTimerText.text = minutesBlack + ":" + secoundsBlack;
        }


        if (Input.GetMouseButtonDown(0))
        {
            if(selectionX >= 0 && selectionY >= 0)
            {
                if(selectedChessman == null)
                {
                    //Select the chessman
                    SelectChessman(selectionX, selectionY);
                    firstX = selectionX;
                    firstY = selectionY;
                }
                else
                {
                    //Move the chessman
                    //Debug.Log("X" + firstX + "Y" + firstY);
                    MoveChessman(selectionX, selectionY);
                    //Debug.Log("X" + selectionX + "Y" + selectionY);

                    if (allowedMoves[selectionX, selectionY])
                        Debug.Log("AT+GOTO" + "(" + firstX + "," + firstY + "," + selectionX + "," + selectionY + ")");
                    else
                    {
                        Debug.Log("illegal move");
                        serialController.SendSerialMessage("AT+ILEGALMOVE");
                    }
                        //serialController.SendSerialMessage("AT+GOTO" + "(" + firstX + "," + firstY + "," + selectionX + "," + selectionY + ")");
                }
            }
        }
    }

    private void SelectChessman(int x,int y)
    {
        if (Chessmans[x, y] == null)
            return;

        if (Chessmans[x, y].isWhite != isWhiteTurn)
            return;

        bool hasAtleastOneMove = false;
        allowedMoves = Chessmans [x, y].PossibleMove ();
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 8; j++)
                if (allowedMoves[i, j])
                    hasAtleastOneMove = true;

        allowedMoves = Chessmans[x, y].PossibleMove();
        selectedChessman = Chessmans[x, y];
        BoardHighlights.Instance.HighlightAllowedMoves(allowedMoves); 
    }

    private void MoveChessman(int x,int y)
    {
        if (allowedMoves[x,y])
        {
            Chessman c = Chessmans[x, y];

            if(c != null && c.isWhite != isWhiteTurn)
            {
                //Capture a piece

                //If it is the king
                if(c.GetType() == typeof(King))
                {
                    if(isWhiteTurn == false) //Sender dobbelt opp med vinneren
                    {
                        serialController.SendSerialMessage("AT+BWIN");
                        Debug.Log("Black Wins");
                    }
                    else
                    {
                        serialController.SendSerialMessage("AT+WWIN");
                        Debug.Log("White Wins");
                    }

                    EndGame();
                    return;
                }

                activeChessman.Remove(c.gameObject);
                Destroy(c.gameObject);
            }

            if(x == enPassantMove[0] && y == enPassantMove[1])
            {
                if (isWhiteTurn)
                    c = Chessmans[x, y - 1];
                else
                    c = Chessmans[x, y + 1];
                activeChessman.Remove(c.gameObject);
                Destroy(c.gameObject);

            }
            enPassantMove[0] = -1;
            enPassantMove[1] = -1;
            if (selectedChessman.GetType() == typeof(Pawn))
            {
                if (y == 7)
                {
                    activeChessman.Remove(selectedChessman.gameObject);
                    Destroy(selectedChessman.gameObject);
                    SpawnChessman(1, x, y);
                    selectedChessman = Chessmans[x, y];
                }else if (y == 0)
                {
                    activeChessman.Remove(selectedChessman.gameObject);
                    Destroy(selectedChessman.gameObject);
                    SpawnChessman(7, x, y);
                    selectedChessman = Chessmans[x, y];
                }
                if (selectedChessman.CurrentY == 1 && y == 3)
                {
                    enPassantMove[0] = x;
                    enPassantMove[1] = y - 1;
                }
                else if (selectedChessman.CurrentY == 6 && y == 4)
                {
                    enPassantMove[0] = x;
                    enPassantMove[1] = y + 1;
                }
            }

            Chessmans [selectedChessman.CurrentX, selectedChessman.CurrentY] = null;
            selectedChessman.transform.position = GetTileCenter(x, y);
            selectedChessman.SetPosition(x, y);
            Chessmans[x, y] = selectedChessman;
            isWhiteTurn = !isWhiteTurn;
        }
        BoardHighlights.Instance.Hidehighlights();
        selectedChessman = null;
    }

    private void UpdateSelection()
    {
        if (!Camera.main)
            return;

        RaycastHit hit;
        if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 25.0f, LayerMask.GetMask("ChessPlane")))
        {
            selectionX = (int)hit.point.x;
            selectionY = (int)hit.point.z;
        }
        else
        {
            selectionX = -1;
            selectionY = -1;
        }
    }

    private void SpawnChessman(int index,int x, int y)
    {
        GameObject go = Instantiate(chessmanPrefabs[index], GetTileCenter(x,y), orientation) as GameObject;
        go.transform.SetParent(transform);
        Chessmans[x, y] = go.GetComponent<Chessman>();
        Chessmans[x, y].SetPosition(x, y);
        activeChessman.Add(go);
    }

    private void SpawnAllChessmans()
    {
        activeChessman = new List<GameObject> ();
        Chessmans = new Chessman[8, 8];
        enPassantMove = new int[2] { -1, -1 };

        //Spawn the white team

        //King
        SpawnChessman(0,4,0);

        //Queen
        SpawnChessman(1,3,0);

        //Rooks
        SpawnChessman(2,0,0);
        SpawnChessman(2,7,0);

        //Bishops
        SpawnChessman(3,2,0);
        SpawnChessman(3,5,0);

        //Knights
        SpawnChessman(4,1,0);
        SpawnChessman(4,6,0);

        //Pawns
        for (int i = 0; i < 8; i++)
            SpawnChessman(5,i,1);

        //Spawn the black team

        //King
        SpawnChessman(6,4,7);

        //Queen
        SpawnChessman(7,3,7);

        //Rooks
        SpawnChessman(8,0,7);
        SpawnChessman(8,7,7);

        //Bishops
        SpawnChessman(9,2,7);
        SpawnChessman(9,5,7);

        //Knights
        SpawnChessman(10,1,7);
        SpawnChessman(10,6,7);

        //Pawns
        for (int i = 0; i < 8; i++)
            SpawnChessman(11,i,6);

    }

    private Vector3 GetTileCenter(int x,int y)
    {
        Vector3 origin = Vector3.zero;
        origin.x += (TILE_SIZE * x) + TILE_OFFSET;
        origin.z += (TILE_SIZE * y) + TILE_OFFSET;
        return origin;
    }

    private void DrawChessboard()
    {
        Vector3 widthLine = Vector3.right * 8;
        Vector3 heightLine = Vector3.forward * 8;

        for (int i = 0; i <= 8; i++)
        {
            Vector3 start = Vector3.forward * i;
            Debug.DrawLine(start, start + widthLine);
            for (int j = 0; j <= 8; j++)
            {

                start = Vector3.right * j;
                Debug.DrawLine(start, start + heightLine);

            }
        }

        //Draw the selection
        if(selectionX >= 0 && selectionY >= 0)
        {
            Debug.DrawLine(
                Vector3.forward * selectionY + Vector3.right * selectionX,
                Vector3.forward * (selectionY + 1) + Vector3.right * (selectionX + 1));

            Debug.DrawLine(
                Vector3.forward * (selectionY + 1 )+ Vector3.right * selectionX,
                Vector3.forward * selectionY + Vector3.right * (selectionX + 1));
        }
    }
    private void EndGame()
    {
        if (isWhiteTurn)
        {
            Debug.Log("White wins");
            SceneManager.LoadScene(2);
        }
        else
        {
            Debug.Log("Black wins");
            SceneManager.LoadScene(3);
        }
        
        /*
        foreach (GameObject go in activeChessman)
            Destroy(go);

        isWhiteTurn = true;
        BoardHighlights.Instance.Hidehighlights();
        SpawnAllChessmans();
        */
    }

    public void restartGame()
    {
        foreach (GameObject go in activeChessman)
            Destroy(go);
        isWhiteTurn = true;
        BoardHighlights.Instance.Hidehighlights();
        SpawnAllChessmans();
        currentBlackTime = startingTime;
        currentWhiteTime = startingTime;
    }

    public void mainMenu()
    {
        SceneManager.LoadScene(1);
    }


    public void OnSubmit()
    {
        float newTime = float.Parse(TimeValue.text);
        Debug.Log(newTime);
        startingTime = newTime;

    }
    public void startGame()
    {
        PanelScript.counter++;
        Debug.Log(PanelScript.counter);
        currentBlackTime = startingTime;
        currentWhiteTime = startingTime;
        Debug.Log("svart tid: "+currentBlackTime+" hvit tid: "+currentWhiteTime+" start: "+startingTime);
        SceneManager.LoadScene(0);
    }
}
