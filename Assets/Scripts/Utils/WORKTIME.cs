using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WORKTIME : MonoBehaviour
{

    public Text openText;
    public Text closeText;

    public OperatingHoursToggle operatingHoursToggle;
    void Start()
    {
        openText.text = operatingHoursToggle.openHour.ToString("00");
        closeText.text = operatingHoursToggle.closeHour.ToString();
    }
}
