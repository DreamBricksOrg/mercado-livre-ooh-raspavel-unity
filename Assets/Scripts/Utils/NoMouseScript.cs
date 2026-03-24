using UnityEngine;

public class NoMouseScript : MonoBehaviour
{

    public KeyCode keyCode;
    static NoMouseScript instance;
    void Start()
    {
        
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            Cursor.visible = false;
        }
        else
        {
            Destroy(gameObject);
        }
        
    }

    void Update()
    {
        if (Input.GetKeyDown(keyCode))
            Cursor.visible = !Cursor.visible;
    }
}