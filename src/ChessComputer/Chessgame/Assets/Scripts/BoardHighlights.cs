using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardHighlights : MonoBehaviour
{
    public static BoardHighlights Instance { set; get; }

    public GameObject highlightPrefab;
    private List<GameObject> highlights; //var private

    private void Start() //var private
    {
        Instance = this;
        highlights = new List<GameObject>();
    }

    private GameObject GetHighlightObject() //var private
    {
        GameObject go = highlights.Find(g => !g.activeSelf); //leter i lista highlights etter gitt condition

        if (go == null) //hvis den ikke finner gjør den følgende
        {
            go = Instantiate(highlightPrefab); //kloner object
            highlights.Add(go); //legger til i lista
        }

        return go; //returnerer klonen
    }

    public void HighlightAllowedMoves(bool[,] moves)
    {
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                if (moves[i, j])
                {
                    GameObject go = GetHighlightObject();
                    go.SetActive(true);
                    go.transform.position = new Vector3(i + 0.5f, 0, j + 0.5f);
                }
            }
        }
    }

    public void Hidehighlights()
    {
        foreach (GameObject go in highlights)
            go.SetActive(false);
       
    }

}
