using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PanelScript : MonoBehaviour
{
    public GameObject Panel;
    public GameObject Block;
    public GameObject Chessboard;
    public static int counter;

    public void showHidePanel()
    {
        counter++;
        Debug.Log(counter);
        if(counter%2 == 0)
        {
            Panel.gameObject.SetActive(false);
            Block.gameObject.SetActive(false);
            Chessboard.gameObject.SetActive(true);
            Time.timeScale = 1;
        }
        else
        {
            Panel.gameObject.SetActive(true);
            Block.gameObject.SetActive(true);
            Chessboard.gameObject.SetActive(false);
            Time.timeScale = 0;
        }
    }


    public void exitGame()
    {
        Application.Quit();
        Debug.Log("Quit");
    }

    public void enterOptions()
    {
        SceneManager.LoadScene(4);
    }


}
