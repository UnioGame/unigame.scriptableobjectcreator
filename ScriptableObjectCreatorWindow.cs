using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityToolbarExtender.Editor
{
    /// <summary>
    /// UI Toolkit-based ScriptableObject creator window without Odin Inspector dependency
    /// </summary>
    public class ScriptableObjectCreatorWindow : EditorWindow
    {
        private static readonly HashSet<Type> ScriptableObjectTypes = GetScriptableObjectTypes();
        
        private string targetFolder = "Assets";
        private ScriptableObject previewObject;
        private Type selectedType;
        private ListView typeListView;
        private InspectorElement inspectorElement;
        private ScrollView scrollView;
        private Label selectedTypeLabel;
        private List<Type> allTypes;
        private bool isResizing;

        [MenuItem("Assets/Create New Scriptable Object", priority = -10000)]
        private static void ShowDialog()
        {
            var path = "Assets";
            var obj = Selection.activeObject;
            
            if (obj != null)
            {
                // Get the path of the currently selected object
                var objPath = AssetDatabase.GetAssetPath(obj);
                
                if (!string.IsNullOrEmpty(objPath))
                {
                    if (AssetDatabase.IsValidFolder(objPath))
                    {
                        // It's already a folder
                        path = objPath;
                    }
                    else if (System.IO.File.Exists(objPath))
                    {
                        // It's a file, get its directory
                        path = System.IO.Path.GetDirectoryName(objPath);
                    }
                }
            }

            var window = GetWindow<ScriptableObjectCreatorWindow>("Create Scriptable Object");
            window.targetFolder = path.TrimEnd('/');
            window.minSize = new Vector2(800, 500);
            Debug.Log($"ScriptableObjectCreator opened. Target folder: {window.targetFolder}");
        }

        private string lastSelectedPath = "";
        private int lastSelectedTypeIndex = -1;
        private int lastSelectedInstanceID = -1;
        private bool isCreatingAsset = false;
        private int assetCountToCreate = 1;
        private string assetName = "";

        private void OnEnable()
        {
            rootVisualElement.Clear();
            BuildUI();
            EditorApplication.update += UpdatePathLabels;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Debug.Log("ScriptableObjectCreatorWindow closed, unsubscribing from events");
            UnsubscribeFromEvents();
        }

        private void OnDestroy()
        {
            Debug.Log("ScriptableObjectCreatorWindow destroyed, cleaning up");
            
            // Ensure events are unsubscribed
            UnsubscribeFromEvents();
            
            // Clean up preview object
            if (previewObject != null && !AssetDatabase.Contains(previewObject))
            {
                DestroyImmediate(previewObject);
            }
        }

        private void UnsubscribeFromEvents()
        {
            EditorApplication.update -= UpdatePathLabels;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            // Force immediate update when selection changes
            UpdatePathLabels();
        }

        private void UpdatePathLabels()
        {
            var currentPath = "Assets";
            
            // Get the project window focused folder path
            var obj = Selection.activeObject;
            
            if (obj != null)
            {
                var objPath = AssetDatabase.GetAssetPath(obj);
                Debug.Log($"Selection changed: obj={obj.name}, objPath={objPath}, isFolder={AssetDatabase.IsValidFolder(objPath)}");
                
                if (!string.IsNullOrEmpty(objPath))
                {
                    if (AssetDatabase.IsValidFolder(objPath))
                    {
                        currentPath = objPath;
                    }
                    else if (System.IO.File.Exists(objPath))
                    {
                        currentPath = System.IO.Path.GetDirectoryName(objPath);
                    }
                }
            }
            
            // Also try ProjectWindowUtil approach for getting selected folder
            // This catches selections in the left panel (favorites/hierarchy)
            if (string.IsNullOrEmpty(currentPath) || currentPath == "Assets")
            {
                var instanceID = Selection.activeInstanceID;
                if (instanceID != 0)
                {
                    var path = AssetDatabase.GetAssetPath(instanceID);
                    if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    {
                        currentPath = path;
                        Debug.Log($"Found folder from instanceID: {path}");
                    }
                }
            }
            
            // Get all selected objects and check if any is a folder
            if (string.IsNullOrEmpty(currentPath) || currentPath == "Assets")
            {
                var selectedObjects = Selection.objects;
                foreach (var selected in selectedObjects)
                {
                    var selectedPath = AssetDatabase.GetAssetPath(selected);
                    if (AssetDatabase.IsValidFolder(selectedPath))
                    {
                        currentPath = selectedPath;
                        Debug.Log($"Found folder from selection: {selectedPath}");
                        break;
                    }
                }
            }

            // Only update if path or type changed to avoid unnecessary redraws
            var selectedIndices = typeListView?.selectedIndices;
            var currentTypeIndex = selectedIndices?.Any() == true ? selectedIndices.First() : -1;
            var currentInstanceID = Selection.activeInstanceID;

            // Always update asset path label since it depends on assetName which might have changed
            var assetPathLabel = rootVisualElement.Q<Label>("assetPathLabel");
            if (assetPathLabel != null)
            {
                var source = typeListView?.itemsSource as List<Type>;
                
                if (currentTypeIndex >= 0 && source != null && currentTypeIndex < source.Count)
                {
                    var selectedType = source[currentTypeIndex];
                    var fileName = string.IsNullOrEmpty(assetName) ? selectedType.Name : assetName;
                    var previewPath = currentPath + "/" + fileName + ".asset";
                    var uniquePath = AssetDatabase.GenerateUniqueAssetPath(previewPath);
                    assetPathLabel.text = $"Asset will be created at: {uniquePath}";
                }
                else
                {
                    assetPathLabel.text = $"Asset will be created at: {currentPath}/[AssetName].asset";
                }
            }

            // Check if path, type, or other state changed to avoid unnecessary redraws
            if (lastSelectedPath == currentPath && lastSelectedTypeIndex == currentTypeIndex && lastSelectedInstanceID == currentInstanceID)
                return;

            Debug.Log($"Updating path labels: {currentPath} (instanceID: {currentInstanceID})");
            lastSelectedPath = currentPath;
            lastSelectedTypeIndex = currentTypeIndex;
            lastSelectedInstanceID = currentInstanceID;

            // Update folder label
            if (rootVisualElement.Q<Label>("folderLabel") is Label folderLabel)
            {
                folderLabel.text = $"Folder: {currentPath}";
            }
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Row;
            root.style.height = Length.Percent(100);

            // Left panel - Type list
            var leftPanel = new VisualElement();
            leftPanel.style.width = 300;
            leftPanel.style.minWidth = 200;
            leftPanel.style.maxWidth = Length.Percent(50);
            leftPanel.style.borderRightWidth = 1;
            leftPanel.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
            leftPanel.style.flexDirection = FlexDirection.Column;
            leftPanel.style.paddingLeft = 5;
            leftPanel.style.paddingRight = 5;
            leftPanel.style.paddingTop = 5;
            leftPanel.style.paddingBottom = 5;

            // Search field
            var searchField = new TextField();
            searchField.value = "";
            searchField.label = "Search types...";
            searchField.style.marginBottom = 10;
            leftPanel.Add(searchField);

            // Type list view
            allTypes = ScriptableObjectTypes
                .OrderBy(t => GetMenuPathForType(t))
                .ToList();

            typeListView = new ListView();
            typeListView.itemsSource = allTypes;
            typeListView.style.flexGrow = 1;
            typeListView.selectionType = SelectionType.Single;
            typeListView.style.marginLeft = 5;
            typeListView.style.marginRight = 5;
            typeListView.style.marginBottom = 5;
            typeListView.fixedItemHeight = 20;

            typeListView.makeItem = () =>
            {
                var container = new VisualElement();
                container.style.height = 20;
                container.style.flexDirection = FlexDirection.Row;
                container.name = "itemContainer";
                
                var label = new Label();
                label.style.paddingLeft = 5;
                label.style.paddingTop = 4;
                label.style.paddingBottom = 4;
                label.style.fontSize = 11;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.flexGrow = 1;
                
                container.Add(label);
                return container;
            };

            typeListView.bindItem = (element, index) =>
            {
                var source = typeListView.itemsSource as List<Type>;
                if (source != null && index >= 0 && index < source.Count)
                {
                    var type = source[index];
                    var typeName = type.Name;
                    var menuPath = GetMenuPathForType(type).Replace("/", " > ");
                    
                    // Create display text with rich text formatting
                    string displayText;
                    if (typeName.Equals(menuPath, System.StringComparison.Ordinal))
                    {
                        // Only type name, colored yellow
                        displayText = $"<color=#FFE64D>{typeName}</color>";
                    }
                    else
                    {
                        // Type name in yellow, path in white
                        displayText = $"<color=#FFE64D>{typeName}</color> — <color=#FFFFFF>{menuPath}</color>";
                    }
                    
                    // Get the container and label from the element
                    var container = element;
                    var label = container.Q<Label>();
                    
                    if (label != null)
                    {
                        label.text = displayText;
                        label.style.textOverflow = TextOverflow.Ellipsis;
                        label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    }
                    
                    // Check if this item is selected
                    var isSelected = typeListView.selectedIndices.Contains(index);
                    
                    // Apply selection styling to container
                    if (isSelected)
                    {
                        // Selected item - add bright border and background
                        container.style.borderLeftWidth = 4;
                        container.style.borderLeftColor = new Color(1f, 0.65f, 0f, 1f);
                        container.style.backgroundColor = new Color(0.4f, 0.3f, 0.1f, 1f);
                        container.style.paddingLeft = 1;
                    }
                    else
                    {
                        // Unselected item - no border
                        container.style.borderLeftWidth = 0;
                        
                        // Alternate row colors for better readability
                        var bgColor = index % 2 == 0
                            ? new Color(0.25f, 0.25f, 0.25f, 0.3f)
                            : new Color(0.3f, 0.3f, 0.3f, 0.2f);
                        container.style.backgroundColor = bgColor;
                        container.style.paddingLeft = 5;
                    }
                }
            };

            typeListView.onSelectionChange += selection =>
            {
                try
                {
                    // Get the selected indices - could be IEnumerable of int or object
                    var selectedList = selection.ToList();
                    Debug.Log($"Selection event fired. Count: {selectedList.Count}");
                    
                    if (selectedList.Count > 0)
                    {
                        var firstItem = selectedList[0];
                        Debug.Log($"First item type: {firstItem.GetType().Name}, value: {firstItem}");
                    }
                    
                    if (selectedList.Count == 0)
                    {
                        selectedType = null;
                        UpdatePreview();
                        return;
                    }

                    // Convert to int safely
                    int index = -1;
                    if (selectedList[0] is int intIndex)
                    {
                        Debug.Log($"Directly cast to int: {intIndex}");
                        index = intIndex;
                    }
                    else if (int.TryParse(selectedList[0].ToString(), out int parsed))
                    {
                        Debug.Log($"Parsed to int: {parsed}");
                        index = parsed;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not convert to int: {selectedList[0]}");
                    }

                    var source = typeListView.itemsSource as List<Type>;
                    Debug.Log($"ItemsSource type: {source?.GetType().Name}, Count: {source?.Count ?? 0}, Index: {index}");
                    
                    if (source != null && index >= 0 && index < source.Count)
                    {
                        selectedType = source[index];
                        Debug.Log($"✓ Selected type successfully: {selectedType.Name}");
                        UpdatePreview();
                        Debug.Log($"Selected type: {selectedType.Name}");
                    }
                    else
                    {
                        Debug.LogWarning($"Index out of range or null source. index={index}, source count={source?.Count ?? 0}");
                    }
                    
                    // Rebuild ListView to update visual styles for all items
                    typeListView.Rebuild();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error selecting type: {ex.Message}\n{ex.StackTrace}");
                }
            };

            leftPanel.Add(typeListView);

            // Search functionality
            searchField.RegisterValueChangedCallback(evt =>
            {
                var searchText = evt.newValue.ToLower();
                var filteredList = allTypes
                    .Where(t =>
                    {
                        var typeName = t.Name.ToLower();
                        var menuPath = GetMenuPathForType(t).ToLower();
                        return string.IsNullOrEmpty(searchText) ||
                               typeName.Contains(searchText) ||
                               menuPath.Contains(searchText);
                    })
                    .ToList();

                typeListView.itemsSource = filteredList;
                typeListView.Rebuild();

                // Keep selection if the selected type is still in filtered list
                if (selectedType != null && filteredList.Contains(selectedType))
                {
                    var index = filteredList.IndexOf(selectedType);
                    if (index >= 0)
                    {
                        typeListView.SetSelection(index);
                    }
                }
                else
                {
                    typeListView.ClearSelection();
                }
            });

            root.Add(leftPanel);

            // Resizable splitter with visual indicators
            var splitter = new VisualElement();
            splitter.style.width = 12;
            splitter.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            
            // Add vertical drag indicators
            var dragIndicator = new VisualElement();
            dragIndicator.style.position = Position.Absolute;
            dragIndicator.style.left = 3;
            dragIndicator.style.top = Length.Percent(50);
            dragIndicator.style.width = 2;
            dragIndicator.style.height = 32;
            dragIndicator.style.backgroundColor = new Color(0.6f, 0.6f, 0.6f);
            dragIndicator.style.marginTop = -16;
            splitter.Add(dragIndicator);

            var startX = 0f;
            var startWidth = 0f;

            splitter.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    isResizing = true;
                    startX = evt.mousePosition.x;
                    startWidth = leftPanel.resolvedStyle.width;
                    splitter.CaptureMouse();
                }
            });

            splitter.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (isResizing)
                {
                    var delta = evt.mousePosition.x - startX;
                    var newWidth = Mathf.Max(200, Mathf.Min(startWidth + delta, root.resolvedStyle.width * 0.5f));
                    leftPanel.style.width = newWidth;
                }
            });

            splitter.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    isResizing = false;
                    splitter.ReleaseMouse();
                }
            });

            root.Add(splitter);

            // Right panel - Inspector and preview
            var rightPanel = new VisualElement();
            rightPanel.style.flexGrow = 1;
            rightPanel.style.flexDirection = FlexDirection.Column;
            rightPanel.style.paddingLeft = 10;
            rightPanel.style.paddingRight = 10;
            rightPanel.style.paddingTop = 10;
            rightPanel.style.paddingBottom = 10;

            // Folder path label
            var folderLabel = new Label($"Folder: {targetFolder}");
            folderLabel.name = "folderLabel";
            folderLabel.style.fontSize = 10;
            folderLabel.style.marginBottom = 10;
            folderLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            rightPanel.Add(folderLabel);

            // Selected type label
            selectedTypeLabel = new Label("Select a ScriptableObject type");
            selectedTypeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            selectedTypeLabel.style.marginBottom = 10;
            selectedTypeLabel.style.fontSize = 12;
            selectedTypeLabel.style.whiteSpace = WhiteSpace.Normal;
            selectedTypeLabel.style.minHeight = 32;
            rightPanel.Add(selectedTypeLabel);

            // Asset path preview label
            var assetPathLabel = new Label("Asset will be created at: Assets/[AssetName].asset");
            assetPathLabel.name = "assetPathLabel";
            assetPathLabel.style.fontSize = 13;
            assetPathLabel.style.marginBottom = 10;
            assetPathLabel.style.color = new Color(0.5f, 0.8f, 0.5f);
            assetPathLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            assetPathLabel.style.whiteSpace = WhiteSpace.Normal;
            rightPanel.Add(assetPathLabel);

            // Asset name field
            var nameField = new TextField("Asset Name");
            nameField.value = assetName;
            nameField.style.marginBottom = 10;
            nameField.RegisterValueChangedCallback(evt =>
            {
                assetName = evt.newValue;
                // Immediately update path labels when asset name changes
                UpdatePathLabels();
            });
            rightPanel.Add(nameField);

            // Count field for creating multiple assets
            var countField = new IntegerField("Count");
            countField.value = assetCountToCreate;
            countField.style.marginBottom = 10;
            countField.RegisterValueChangedCallback(evt =>
            {
                assetCountToCreate = Mathf.Max(1, evt.newValue);
                countField.value = assetCountToCreate;
            });
            rightPanel.Add(countField);

            // Button (placed before inspector to ensure visibility)
            var createButton = new Button(CreateAsset);
            createButton.text = "Create Asset";
            createButton.style.height = 32;
            createButton.style.fontSize = 11;
            createButton.style.marginBottom = 10;
            rightPanel.Add(createButton);

            // Inspector scroll view
            scrollView = new ScrollView();
            scrollView.style.flexGrow = 1;
            scrollView.style.marginBottom = 0;
            scrollView.style.minHeight = 200;

            inspectorElement = new InspectorElement();
            inspectorElement.style.flexGrow = 1;
            scrollView.Add(inspectorElement);
            rightPanel.Add(scrollView);

            root.Add(rightPanel);

            // Set focus to search field by default
            searchField.Focus();
        }

        private void UpdatePreview()
        {
            // Ensure inspector element exists
            if (inspectorElement == null)
            {
                Debug.LogError("InspectorElement is null!");
                return;
            }

            // Clean up old preview object
            if (previewObject != null && !AssetDatabase.Contains(previewObject))
            {
                DestroyImmediate(previewObject);
            }

            inspectorElement.Unbind();

            if (selectedType != null && !selectedType.IsAbstract)
            {
                try
                {
                    Debug.Log($"Creating preview for type: {selectedType.Name}");
                    previewObject = CreateInstance(selectedType);
                    if (previewObject != null)
                    {
                        var typeName = selectedType.Name;
                        var menuPath = GetMenuPathForType(selectedType).Replace("/", " > ");
                        selectedTypeLabel.text = $"{typeName}\n{menuPath}";
                        selectedTypeLabel.style.whiteSpace = WhiteSpace.Normal;

                        // Create a new SerializedObject and bind it
                        var serializedObject = new SerializedObject(previewObject);
                        Debug.Log($"SerializedObject created: {serializedObject != null}");
                        inspectorElement.Bind(serializedObject);
                        Debug.Log($"Inspector bound successfully");
                    }
                    else
                    {
                        Debug.LogError($"Failed to create instance of {selectedType.Name}");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to create preview for type {selectedType.Name}: {ex.Message}\n{ex.StackTrace}");
                    selectedTypeLabel.text = "Error creating preview";
                }
            }
            else
            {
                previewObject = null;
                selectedTypeLabel.text = "Select a ScriptableObject type";
            }
        }

        private void CreateAsset()
        {
            try
            {
                isCreatingAsset = true;

                // Get the current selected folder path (not from opening time, but from NOW)
                var currentTargetFolder = "Assets";
                var obj = Selection.activeObject;
                
                if (obj != null)
                {
                    var objPath = AssetDatabase.GetAssetPath(obj);
                    
                    if (!string.IsNullOrEmpty(objPath))
                    {
                        if (AssetDatabase.IsValidFolder(objPath))
                        {
                            currentTargetFolder = objPath;
                        }
                        else if (System.IO.File.Exists(objPath))
                        {
                            currentTargetFolder = System.IO.Path.GetDirectoryName(objPath);
                        }
                    }
                }
                
                Debug.Log($"Creating asset in folder: {currentTargetFolder}");

                // Double-check selectedType from ListView state
                var selectedIndices = typeListView.selectedIndices;
                if (!selectedIndices.Any())
                {
                    Debug.LogError("No selection in ListView");
                    EditorUtility.DisplayDialog("Error", "Please select a valid ScriptableObject type first", "OK");
                    return;
                }

                var selectedIndex = selectedIndices.First();
                var currentItemsSource = typeListView.itemsSource as List<Type>;
                if (currentItemsSource == null || selectedIndex < 0 || selectedIndex >= currentItemsSource.Count)
                {
                    Debug.LogError($"Invalid ListView state: itemsSource={currentItemsSource}, index={selectedIndex}");
                    EditorUtility.DisplayDialog("Error", "Please select a valid ScriptableObject type first", "OK");
                    return;
                }

                selectedType = currentItemsSource[selectedIndex];
                Debug.Log($"CreateAsset called. selectedType = {(selectedType != null ? selectedType.Name : "NULL")}");

                if (selectedType == null || selectedType.IsAbstract)
                {
                    Debug.LogError($"Invalid selectedType: {(selectedType == null ? "NULL" : selectedType.Name + " (Abstract: " + selectedType.IsAbstract + ")")}");
                    EditorUtility.DisplayDialog("Error", "Please select a valid ScriptableObject type first", "OK");
                    return;
                }

                // Create preview object if it doesn't exist
                if (previewObject == null)
                {
                    previewObject = CreateInstance(selectedType);
                }

                if (previewObject == null)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create preview object", "OK");
                    return;
                }

                var fileName = string.IsNullOrEmpty(assetName) ? selectedType.Name : assetName;
                var dest = currentTargetFolder + "/" + fileName + ".asset";
                dest = AssetDatabase.GenerateUniqueAssetPath(dest);

                // Create multiple assets if count > 1
                ScriptableObject lastCreatedAsset = null;
                for (int i = 0; i < assetCountToCreate; i++)
                {
                    // Create a fresh object for each asset
                    var assetObject = CreateInstance(selectedType);
                    if (assetObject == null)
                    {
                        EditorUtility.DisplayDialog("Error", "Failed to create object instance", "OK");
                        return;
                    }

                    // Generate unique path for each asset
                    var assetPath = dest;
                    if (i > 0)
                    {
                        // For subsequent assets, add a number suffix
                        assetPath = currentTargetFolder + "/" + fileName + "_" + (i + 1) + ".asset";
                    }
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

                    // Create the asset
                    AssetDatabase.CreateAsset(assetObject, assetPath);
                    lastCreatedAsset = (ScriptableObject)AssetDatabase.LoadAssetAtPath(assetPath, selectedType);
                    
                    Debug.Log($"Asset {i + 1} created successfully at: {assetPath}");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Select the last created asset in the inspector
                if (lastCreatedAsset != null)
                {
                    Selection.activeObject = lastCreatedAsset;
                    EditorGUIUtility.PingObject(lastCreatedAsset);
                }

                // Restore the selection in ListView instead of clearing it
                // This keeps the UI state consistent and prevents focus loss
                if (typeListView.itemsSource is List<Type> source && selectedIndex >= 0 && selectedIndex < source.Count)
                {
                    typeListView.SetSelection(selectedIndex);
                    Debug.Log($"Selection restored to index {selectedIndex}");
                }
            }
            finally
            {
                isCreatingAsset = false;
            }
        }

        private string GetMenuPathForType(Type type)
        {
            var path = "";

            if (type.Namespace == null)
                return type.Name;

            foreach (var part in type.Namespace.Split('.'))
            {
                path += part + "/";
            }

            return path + type.Name;
        }

        private static HashSet<Type> GetScriptableObjectTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(t =>
                    t.IsClass &&
                    !t.IsAbstract &&
                    typeof(ScriptableObject).IsAssignableFrom(t) &&
                    !typeof(EditorWindow).IsAssignableFrom(t) &&
                    !typeof(UnityEditor.Editor).IsAssignableFrom(t))
                .ToHashSet();
        }
    }
}
