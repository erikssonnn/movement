using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.EventSystems;

/*
* An in-game console made for some of my projects. Commands can be added and removed any time.
* Carl Eriksson
* 2023-06-04
*/

public class ConsoleController : MonoBehaviour {
    [SerializeField] private GameObject console = null;
    [SerializeField] private Text output = null;
    [SerializeField] private Text inputText= null;
    private static readonly string[] commands = { "debug", "speed", "cspeed", "noclip", "sanity"};
    private bool active = false;
    private string lastCommand = "";
    private bool debug = false;
    private string inputString = "";
    private string debugString = "debug";

    private MovementController movementController = null;
    private FreeCameraController freeCameraController = null;
    private CharacterController characterController = null;
    
    private void Start() {
        movementController = FindObjectOfType<MovementController>();
        freeCameraController = FindObjectOfType<FreeCameraController>();
        if (movementController != null) characterController = movementController.GetComponent<CharacterController>();
        console.SetActive(false);
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Tilde)) {
            ToggleConsole();
        }

        if (Input.GetKeyDown(KeyCode.UpArrow)) {
            inputString = lastCommand;
        }

        if (active && Input.GetKeyDown(KeyCode.Return)) {
            Enter();
        }

        if (!active) return;
        foreach(char c in Input.inputString) {
            if (c == '\b') {
                if (inputString.Length > 0) {
                    inputString = inputString.Substring(0, inputString.Length - 1);
                }
            } else if (c == '\u0020') {
                inputString += " ";
            } else {
                inputString += c;
            }
        }
        inputText.text = inputString + "_";
    }

    private void ToggleConsole() {
        active = !active;
        console.SetActive(active);
        movementController.enabled = freeCameraController.enabled == true ? false : !active;
        MouseController.instance.ToggleMouse();
        Time.timeScale = active ? 0.0f : 1.0f;
        EventSystem.current.SetSelectedGameObject(inputText.gameObject);
    }

    private static bool IsValidCommand(string str) {
        return commands.Any(command => command.Trim() == str);
    }

    private void PrintConsole(string str) {
        float h = output.preferredHeight;

        if(h > output.rectTransform.rect.height) {
            output.text = "<--CLEARED CONSOLE-->\n";
        }

        output.text += str;
    }

    private void Enter() {
        string inputStr = inputText.text.Replace("\n", "").Replace("_", "").Trim();
        lastCommand = inputStr;
        string[] splitStr = inputStr.Split(' ');
        string commandStr = splitStr[0].Trim();

        if (!IsValidCommand(commandStr)) {
            PrintConsole(inputStr + " <-- INVALID COMMAND\n");
            inputString = "";
            return;
        }

        if (splitStr.Length < 2) {
            PrintConsole(inputStr + " <-- COMMAND NEEDS VALUE\n");
            inputString = "";
            return;
        }

        if (int.TryParse(splitStr[1], out int value)) {
            value = int.Parse(splitStr[1]);
        } else {
            PrintConsole(inputStr + " <-- INVALID VALUE\n");
            inputString = "";
            return;
        }

        if (IsValidCommand(commandStr) && value != -1) {
            PrintConsole(inputStr + "\n");
            ExecuteCommand(commandStr, value);
        }

        inputString = "";
    }

    private void ExecuteCommand(string command, int value) { 
        bool b = value == 1 ? true : false;
        switch (command) {
            case "debug":
                debug = b;
                Camera cam = Camera.main;
                if (cam == null) {
                    Debug.LogError("CANT FIND MAIN CAMERA");
                    Debug.Break();
                    return;
                }
                cam.cullingMask = value == 1 ? cam.cullingMask |= (1 << LayerMask.NameToLayer(debugString)) : cam.cullingMask &= ~(1 << LayerMask.NameToLayer(debugString));
                break;
            case "speed":
                movementController.Speed = value;
                break;
            case "noclip":
                movementController.enabled = !b;
                characterController.enabled = !b;
                freeCameraController.enabled = b;
                break;
            case "sanity":
                SanityController.instance.ChangeSanity(value);
                break;
            default:
                UnityEngine.Debug.LogError("SWITCH CASE FELL BACK TO DEFAULT");
                break;
        }
    }
}
