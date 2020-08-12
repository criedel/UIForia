using System;
using System.Collections.Generic;
using UIForia.Parsing;
using UIForia.Util;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UIForia.Editor {

    [CustomEditor(typeof(UIViewBehavior))]
    public class UIViewBehaviorInspector : UnityEditor.Editor {

        private Type[] types;
        private string[] names;
        private bool didEnable;

        [DidReloadScripts]
        private static void OnScriptsReloaded() {
            // todo try to track if pre-compiled templates are out of date or not
        }

        public void OnEnable() {
            didEnable = true;
            LightList<ProcessedType> typeData = TypeProcessor.GetTemplateTypes();

            List<Type> validTypes = new List<Type>();
            for (int i = 0; i < typeData.size; i++) {
                if (typeData[i].rawType.Assembly.FullName.StartsWith("UIForia")) {
                    continue;
                }

                validTypes.Add(typeData[i].rawType);
            }

            types = new Type[validTypes.Count];
            names = new string[types.Length];
            for (int i = 0; i < types.Length; i++) {
                types[i] = validTypes[i];
                names[i] = validTypes[i].FullName;
            }
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            UIViewBehavior behavior = (UIViewBehavior) target;
            string typeName = serializedObject.FindProperty("typeName").stringValue;
            if (behavior.type != null && typeName != behavior.type.AssemblyQualifiedName) {
                behavior.type = Type.GetType(typeName);
            }
            else if (behavior.type == null) {
                behavior.type = Type.GetType(typeName);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Root Template");

            if (types == null || types.Length == 0) {
                if (!didEnable) OnEnable();
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (behavior.type == null) {
                behavior.type = types[0];
            }

            int index = Array.IndexOf(types, behavior.type);
            int newIndex = EditorGUILayout.Popup(index, names);
            EditorGUILayout.EndHorizontal();

            if (index != newIndex) {
                behavior.type = types[newIndex];
                behavior.typeName = behavior.type.AssemblyQualifiedName;
                EditorSceneManager.MarkSceneDirty(behavior.gameObject.scene);
            }

            EditorGUILayout.BeginHorizontal();
            behavior.styleBasePath = EditorGUILayout.TextField("Style Base Path", behavior.styleBasePath);
            EditorGUILayout.EndHorizontal();

            behavior.usePreCompiledTemplates = GUILayout.Toggle(behavior.usePreCompiledTemplates, "Use Precompiled");

            if (GUILayout.Button("Generate Code")) {
                TemplateCodeGenerator.Generate(behavior.type, behavior.GetTemplateSettings(behavior.type));

//                TemplateCompiler compiler = new TemplateCompiler(settings);
//                
//                // maybe this should also know the root type for an application
//                PreCompiledTemplateData compiledOutput = new PreCompiledTemplateData(settings);
//
//                compiler.CompileTemplates(behavior.type, compiledOutput);
//
//                compiledOutput.GenerateCode();
            }

            EditorGUILayout.ObjectField(serializedObject.FindProperty("camera"));
            serializedObject.FindProperty("typeName").stringValue = behavior.typeName;
            serializedObject.ApplyModifiedProperties();

            if (EditorApplication.isPlaying) {
                if (behavior.application == null) return;
                EditorGUILayout.BeginHorizontal();
                float dpi = behavior.application.DPIScaleFactor;
                if (newDpi <= 0) newDpi = dpi;
                newDpi = EditorGUILayout.FloatField("DPI Override", newDpi);
                if (GUILayout.Button("Update DPI")) {
                    behavior.application.DPIScaleFactor = Mathf.Max(1, newDpi);
                }

                if (GUILayout.Button("Reset DPI")) {
                    newDpi = behavior.application.DPIScaleFactor = Application.originalDpiScaleFactor;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private float newDpi;

    }

}