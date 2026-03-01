using System;
using Game.Services.Saves;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI.Saves
{
    public class SaveMenuUI : MonoBehaviour
    {
        private UIDocument _uiDocument;

        private ListView _listView;
        private Button _setApiKeyButton;
        private VisualElement _modal;

        private SetApiKeyPanel _setApiKeyPanel;

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();

            var root = _uiDocument.rootVisualElement;
            var saves = SaveService.ListSaves();

            _listView = root.Q<ListView>();
            _modal = root.Q("Modal");
            _setApiKeyButton = root.Q<Button>("SetApiKeyButton");
            _setApiKeyPanel = new(root.Q("SetApiKeyPanel"));

            _setApiKeyPanel.onClose.AddListener(() =>
            {
                _setApiKeyPanel.SetEnabled(false);
                _modal.visible = false;
            });

            _setApiKeyButton.RegisterCallback<ClickEvent>(_ =>
            {
                _modal.visible = true;
                _setApiKeyPanel.SetEnabled(true);
            });

            _listView.itemsSource = saves;

            _listView.bindItem = (item, index) =>
            {
                var (saveName, save) = saves[index];
                item.Q<Label>("SaveNameLabel").text = saveName;
                item.Q<Label>("SaveCreatedAt").text = $"Turn {save.Game.Turn} ({save.Game.CurrentYear}/{save.Game.CurrentMonth:00})";

                item.Q<Button>().RegisterCallback<ClickEvent>(_ =>
                {
                    SaveService.SetCurrentSave(save);
                    SceneManager.LoadScene("Game");
                });
            };

            _listView.Rebuild();
        }

        void Update()
        {

        }
    }
}