using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GaugeController : MonoBehaviour
{
    PlayerControls controls;
    public Engine engineScript;

    public Slider throttleSlider;
    public Slider brakeSlider;
    public TextMeshProUGUI gearText;

    private float throttleInput;
    private float brakeInput;


    void Awake()
    {
        controls = new PlayerControls();
    }

    void OnEnable()
    {
        controls.Gameplay.Enable();
    }

    void OnDisable()
    {
        controls.Gameplay.Disable();
    }

    // Update is called once per frame
    void Update()
    {
        float targetThrottle = controls.Gameplay.Throttle.ReadValue<float>();
        throttleInput = Mathf.Lerp(throttleInput, targetThrottle, 3f * Time.deltaTime);
        float targetBrake = controls.Gameplay.Brake.ReadValue<float>();
        brakeInput = Mathf.Lerp(brakeInput, targetBrake, 5f * Time.deltaTime);

        UpdateSliders();
        UpdateText();
    }

    private void UpdateSliders()
    {
        throttleSlider.value = throttleInput;
        brakeSlider.value = brakeInput;
    }

    private void UpdateText()
    {
        if (engineScript.isShifting)
        {
            gearText.SetText("---");
        }
        else
        {
            if (!engineScript.isReversing)
                gearText.SetText("Gear " + (1 + engineScript.gearIndex));
            else
                gearText.SetText("Gear R");
        }
    }
}
