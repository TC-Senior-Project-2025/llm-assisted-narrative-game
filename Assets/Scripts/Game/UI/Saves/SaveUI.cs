using System;
using System.Collections.Generic;
using Game.Services;
using Game.Services.Saves;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.Saves
{
    public class SaveUI : MonoBehaviour
    {
        private UIDocument _uiDocument;
        private VisualElement _panel;

        private ListView _listView;
        private TextField _saveNameField;

        private Button _saveButton;

        private List<(string, SaveData)> saves;

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;
            _panel = root.Q("SavePanel");

            _listView = _panel.Q<ListView>("SaveList");
            _saveNameField = _panel.Q<TextField>("SaveNameField");

            _listView.bindItem = (item, index) =>
            {
                var (saveName, save) = saves[index];
                item.Q<Label>("SaveNameLabel").text = saveName;
                item.Q<Label>("TurnLabel").text = $"Turn {save.Game.Turn} ({save.Game.CurrentYear}/{save.Game.CurrentMonth:00})";

                var button = item.Q<Button>();
                button.RegisterCallback<ClickEvent>(_ =>
                {
                    _saveNameField.value = save.Game.SaveName;
                });
            };

            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => SetEnabled(false));
            _saveButton = _panel.Q<Button>("SaveButton");
            _saveButton.RegisterCallback<ClickEvent>(_ => OnSave());

            SetEnabled(false);
        }

        private void OnSave()
        {
            SaveService.Save(_saveNameField.value, GameService.Main.State.CurrentValue);
            SetEnabled(false);
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;

            if (isEnabled)
            {
                saves = SaveService.ListSaves();
                _listView.itemsSource = saves;
                _listView.Rebuild();
                _saveNameField.value = GameService.Main.State.CurrentValue.Game.SaveName;
            }
        }
    }
}