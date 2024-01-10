using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PartScaler
{
    public struct ScaleChange
    {
        public float scale;
        public float relativeScale;

        public ScaleChange(float scale, float relativeScale = 0f)
        {
            this.scale = scale;
            this.relativeScale = relativeScale == 0f ? scale : relativeScale;
        }
    }

    public class PartScaler : PartModule, IPartCostModifier, IPartMassModifier
    {
        private const string PAW_GROUP_NAME = "PartResizer";

        private static string LOC_PartResizer = "Part resizer";
        private static string LOC_ScaleMode = "Scale mode";
        private static string LOC_Scale = "Scale";
        private static string LOC_Disabled = "Disabled";
        private static string LOC_Free = "Free";
        private static string LOC_StackPresets = "Stack presets";
        private static string LOC_StackPreset = "Stack preset";

        private static string[] _scaleModeOptionsFreeOnly;
        private static string[] _scaleModeOptionsAll;

        private static string[] ScaleModeOptionsFreeOnly => _scaleModeOptionsFreeOnly == null 
            ? _scaleModeOptionsFreeOnly = new string[] { LOC_Disabled, LOC_Free } 
            : _scaleModeOptionsFreeOnly;

        private static string[] ScaleModeOptionsAll => _scaleModeOptionsAll == null 
            ? _scaleModeOptionsAll = new string[] { LOC_Disabled, LOC_StackPresets, LOC_Free } 
            : _scaleModeOptionsAll;

        public enum ScaleMode
        {
            Disabled = 0,
            Free = 1,
            StackPreset = 2
        }

        [KSPField(guiActiveEditor = true)]
        [UI_Cycle(scene = UI_Scene.Editor)]
        public int scaleModeIdx = 0;

        [KSPField(guiActiveEditor = true)]
        [UI_FloatRange(scene = UI_Scene.Editor)]
        public float freeScale;

        [KSPField(guiActiveEditor = true)]
        [UI_ChooseOption(scene = UI_Scene.Editor)]
        public int stackPresetIdx;

        [KSPField]
        public float minScale = 0.5f;

        [KSPField]
        public float maxScale = 2f;

        [KSPField(isPersistant = true)]
        public ScaleMode scaleMode = ScaleMode.Disabled;

#if DEBUG
        [KSPField(isPersistant = true, guiActiveEditor = true)]
#else
        [KSPField(isPersistant = true)]
#endif
        public float scale = 1f;

        [KSPField(isPersistant = true)]
        public float stackSize = 0f;

        [KSPField(isPersistant = true)] 
        public Vector3 pristineModelScale = Vector3.zero;

        private float massModifier;
        private float costModifier;

        private string[] availablePresetProfiles;

        public bool UseSizeScale => stackSize > 0f;

        public bool IsScaled => scale != 1f;

        private void OnLoad2(ConfigNode node)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                DetectStackSize();
            }
        }

        private void DetectStackSize()
        {
            if (!part.attachRules.stack || !part.attachRules.allowStack)
                return;

            Bounds bounds = new Bounds(part.transform.position, Vector3.zero);
            foreach (Collider collider in part.GetPartColliders())
            {
                if (!collider.gameObject.activeInHierarchy || collider.gameObject.layer != 0)
                    continue;

                bounds.Encapsulate(collider.bounds);
            }

            Vector3 boundsSize = bounds.size;
            float minProfile = Math.Min(boundsSize.x, boundsSize.z);
            if (minProfile < 0.1f)
                return;

            bool xCentered = Math.Abs(bounds.center.x - part.transform.position.x) < 0.1f;
            bool zCentered = Math.Abs(bounds.center.z - part.transform.position.z) < 0.1f;

            float relativeAsymmetry = Math.Abs(boundsSize.x - boundsSize.z) / minProfile;

            if (xCentered && zCentered && relativeAsymmetry < 0.1f)
            {
                float averageProfile = (boundsSize.x + boundsSize.z) * 0.5f;
                foreach (StackPreset stackPreset in StackPreset.presets)
                {
                    if (!stackPreset.isAutoProfile)
                        continue;

                    if (Math.Abs(averageProfile - stackPreset.size) < 0.25f)
                    {
                        stackSize = stackPreset.size;
                        Debug.Log($"[PartScaler] Autodetected stack size {stackPreset.title} for {part.name}");
                        return;
                    }
                }
            }

            if (relativeAsymmetry < 0.75f)
            {
                foreach (StackPreset stackPreset in StackPreset.presets)
                {
                    if (!stackPreset.isAutoProfile)
                        continue;

                    if ((xCentered && Math.Abs(boundsSize.x - stackPreset.size) < 0.1f) 
                        || (zCentered && Math.Abs(boundsSize.z - stackPreset.size) < 0.1f))
                    {
                        stackSize = stackPreset.size;
                        Debug.Log($"[PartScaler] Autodetected stack size {stackPreset.title} for {part.name}");
                        return;
                    }
                }
            }
        }

        private void SetupPAW()
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
                return;

            BasePAWGroup pawGroup = new BasePAWGroup(PAW_GROUP_NAME, LOC_PartResizer, false);

            BaseField scaleModeField = Fields[nameof(scaleModeIdx)];
            scaleModeField.guiName = LOC_ScaleMode;
            scaleModeField.group = pawGroup;
            UI_Cycle scaleModeCtrl = (UI_Cycle)scaleModeField.uiControlEditor;
            scaleModeField.OnValueModified += OnPAWScaleModeModified;
            scaleModeCtrl.stateNames = UseSizeScale ? ScaleModeOptionsAll : ScaleModeOptionsFreeOnly;
            scaleModeIdx = ScaleModeToControlIndex();

            BaseField freeScaleField = Fields[nameof(freeScale)];
            freeScaleField.guiName = LOC_Scale;
            freeScaleField.group = pawGroup;
            UI_FloatRange freeScaleCtrl = (UI_FloatRange)freeScaleField.uiControlEditor;
            freeScaleField.OnValueModified += OnPAWFreeScaleModified;

            if (!UseSizeScale)
            {
                if (scaleMode == ScaleMode.StackPreset)
                    scaleMode = ScaleMode.Free;

                freeScale = 100f;
                freeScaleField.guiFormat = @"0\%";
                freeScaleCtrl.stepIncrement = 1f;
                freeScaleCtrl.minValue = minScale * 100f; // TODO: hardcode a lower limit based on min mass
                freeScaleCtrl.maxValue = maxScale * 100f;
            }
            else
            {
                freeScale = stackSize;
                freeScaleField.guiFormat = "0.000m";
                freeScaleCtrl.stepIncrement = 0.005f;
                freeScaleCtrl.minValue = minScale * stackSize;
                freeScaleCtrl.maxValue = maxScale * stackSize;

                BaseField stackPresetField = Fields[nameof(stackPresetIdx)];
                stackPresetField.guiName = LOC_StackPreset;
                stackPresetField.group = pawGroup;
                UI_ChooseOption stackPresetCtrl = (UI_ChooseOption)stackPresetField.uiControlEditor;
                stackPresetField.OnValueModified += OnPAWStackPresetModified;

                StackPreset.GetPresets(freeScaleCtrl.minValue, freeScaleCtrl.maxValue, stackSize, out stackPresetCtrl.options, out stackPresetCtrl.display, out stackPresetIdx);
                availablePresetProfiles = stackPresetCtrl.options;
            }

            OnPAWScaleModeModified(null);
        }

        private void OnPAWStackPresetModified(object newValue)
        {
            string stackProfile = availablePresetProfiles[stackPresetIdx];
            StackPreset stackPreset = StackPreset.GetPresetForProfile(stackProfile);
            ChangeScale(stackPreset.size / stackSize);
        }

        private void OnPAWFreeScaleModified(object newValue)
        {
            float newScale = UseSizeScale ? freeScale / stackSize : freeScale / 100f;
            ChangeScale(newScale);
        }

        private void OnPAWScaleModeModified(object newValue)
        {
            scaleMode = ControlIndexToScaleMode();

            switch (scaleMode)
            {
                case ScaleMode.Disabled:
                    Fields[nameof(freeScale)].guiActiveEditor = false;
                    Fields[nameof(stackPresetIdx)].guiActiveEditor = false;
                    ChangeScale(1f);
                    break;
                case ScaleMode.Free:
                    Fields[nameof(freeScale)].guiActiveEditor = true;
                    Fields[nameof(stackPresetIdx)].guiActiveEditor = false;
                    if (UseSizeScale)
                        freeScale = stackSize * scale;
                    else
                        freeScale = scale * 100f;
                    break;
                case ScaleMode.StackPreset:
                    Fields[nameof(freeScale)].guiActiveEditor = false;
                    Fields[nameof(stackPresetIdx)].guiActiveEditor = true;
                    StackPreset stackPreset = StackPreset.GetClosestStackPreset(stackSize * scale);
                    for (int i = 0; i < availablePresetProfiles.Length; i++)
                    {
                        if (availablePresetProfiles[i] == stackPreset.profile)
                        {
                            stackPresetIdx = i;
                            ChangeScale(stackPreset.size / stackSize);
                            break;
                        }
                    }
                    break;
            }
        }

        private int ScaleModeToControlIndex()
        {
            switch (scaleMode)
            {
                default: return 0;
                case ScaleMode.Free: return UseSizeScale ? 2 : 1;
                case ScaleMode.StackPreset: return 1;
            }
        }

        private ScaleMode ControlIndexToScaleMode()
        {
            switch (scaleModeIdx)
            {
                default: return ScaleMode.Disabled;
                case 1: return UseSizeScale ? ScaleMode.StackPreset : ScaleMode.Free;
                case 2: return ScaleMode.Free;
            }
        }


        private void ChangeScale(float newScale)
        {
            ScaleChange scaleChange = new ScaleChange(newScale, newScale / scale);
            scale = newScale;

            ScalePart(scaleChange, true, false);
            ScaleDragCubes(scaleChange, false);

            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }
















        private bool _setupRun;

        /// <summary>
        /// Sets up values from ScaleType, creates updaters, and sets up initial values.
        /// </summary>
        protected virtual void Setup()
        {
            if (_setupRun)
                return;

            if (part.partInfo.partPrefab == null)
            {
                _setupRun = true;
                enabled = false;
                return;
            }

            if (IsScaled)
            {
                ScaleChange scaleChange = new ScaleChange(scale);
                ScalePart(scaleChange, false, true);
                ScaleDragCubes(scaleChange, true);
            }

            _setupRun = true;
        }

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                DetectStackSize();
            }
            else
            {
                if (HighLogic.LoadedSceneIsEditor || IsScaled)
                    Setup();
                else
                    enabled = false;
            }
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                Setup();
                SetupPAW();
            }
        }

        /// <summary>
        /// Updates properties that change linearly with scale.
        /// </summary>
        /// <param name="moveParts">Whether or not to move attached parts.</param>
        /// <param name="absolute">Whether to use absolute or relative scaling.</param>
        private void ScalePart(ScaleChange scaleChange, bool moveParts, bool absolute)
        {
            ScalePartModel(scaleChange);
            ScaleVariantsAttachNodes(scaleChange, absolute);
            ScaleAttachNodes(scaleChange, moveParts, absolute);
        }

        private void ScalePartModel(ScaleChange scaleChange)
        {
            Transform modelTransform = part.transform.Find("model");
            if (modelTransform == null)
            {
                Debug.LogError($"[PartScaler] Model not found, can't resize part {part.partInfo.name}");
                return;
            }
            
            part.rescaleFactor = part.partInfo.partPrefab.rescaleFactor * scaleChange.scale;

            if (pristineModelScale == Vector3.zero)
                pristineModelScale = modelTransform.localScale;

            modelTransform.localScale = pristineModelScale * scaleChange.scale;
        }

        private void ScaleVariantsAttachNodes(ScaleChange scaleChange, bool absolute)
        {
            if (part.variants == null)
                return;

            for (int i = 0; i < part.variants.variantList.Count; i++)
            {
                PartVariant partVariant = part.variants.variantList[i];
                PartVariant prefabVariant = part.partInfo.partPrefab.variants.variantList[i];

                for (int j = 0; j < partVariant.AttachNodes.Count; j++)
                {
                    MoveNode(scaleChange, partVariant.AttachNodes[j], prefabVariant.AttachNodes[j], false, absolute);
                }

                if (partVariant.SrfAttachNode != null)
                {
                    MoveNode(scaleChange, partVariant.SrfAttachNode, prefabVariant.SrfAttachNode, false, absolute);
                }
            }
        }

        private void ScaleAttachNodes(ScaleChange scaleChange, bool moveParts, bool absolute)
        {
            foreach (AttachNode attachNode in part.attachNodes)
            {
                bool nodeIsOnVariant = false;
                if (part.variants != null && part.variants.SelectedVariant != null)
                {
                    foreach (AttachNode variantNode in part.variants.SelectedVariant.AttachNodes)
                    {
                        if (attachNode.id == variantNode.id)
                        {
                            nodeIsOnVariant = true;
                            if (moveParts)
                                MovePart(scaleChange, attachNode.attachedPart, variantNode.position, attachNode.position);

                            attachNode.position = variantNode.position;
                            attachNode.originalPosition = variantNode.originalPosition;
                            attachNode.size = variantNode.size;
                            break;
                        }
                    }
                }

                if (nodeIsOnVariant)
                    continue;

                AttachNode pristineNode = null;
                foreach (AttachNode prefabNode in part.partInfo.partPrefab.attachNodes)
                {
                    if (attachNode.id == prefabNode.id)
                    {
                        pristineNode = prefabNode;
                    }
                }

                if (pristineNode == null)
                {
                    Debug.LogError($"[PartScaler] Error scaling {part.partInfo.name}, node {attachNode.id} not found on prefab or variant");
                    continue;
                }

                MoveNode(scaleChange, attachNode, pristineNode, moveParts, absolute);
            }

            if (part.srfAttachNode != null)
            {
                if (part.variants != null && part.variants.SelectedVariant?.SrfAttachNode != null)
                {
                    if (moveParts)
                        MovePart(scaleChange, part.srfAttachNode.attachedPart, part.variants.SelectedVariant.SrfAttachNode.position, part.srfAttachNode.position);

                    part.srfAttachNode.position = part.variants.SelectedVariant.SrfAttachNode.position;
                    part.srfAttachNode.originalPosition = part.variants.SelectedVariant.SrfAttachNode.originalPosition;
                    part.srfAttachNode.size = part.variants.SelectedVariant.SrfAttachNode.size;
                }
                else
                {
                    MoveNode(scaleChange, part.srfAttachNode, part.partInfo.partPrefab.srfAttachNode, moveParts, absolute);
                }
            }

            if (moveParts)
            {
                int numChilds = part.children.Count;
                for (int i = 0; i < numChilds; i++)
                {
                    var child = part.children[i];
                    if (child.srfAttachNode == null || child.srfAttachNode.attachedPart != part)
                        continue;

                    var attachedPosition = child.transform.localPosition + child.transform.localRotation * child.srfAttachNode.position;
                    var targetPosition = attachedPosition * scaleChange.relativeScale;
                    child.transform.Translate(targetPosition - attachedPosition, part.transform);
                }
            }
        }

        /// <summary>
        /// Change the size of <paramref name="node"/> to reflect the new size of the part it's attached to.
        /// </summary>
        /// <param name="node">The node to resize.</param>
        /// <param name="baseNode">The same node, as found on the prefab part.</param>
        private void ScaleAttachNode(AttachNode node, AttachNode baseNode)
        {
            float tmpBaseNodeSize = baseNode.size;
            if (tmpBaseNodeSize == 0)
                tmpBaseNodeSize = 0.5f;

            node.size = (int)(tmpBaseNodeSize * scale + 0.49);
            
            if (node.size < 0)
                node.size = 0;
        }

        /// <summary>
        /// Moves <paramref name="node"/> to reflect the new scale. If <paramref name="movePart"/> is true, also moves attached parts.
        /// </summary>
        /// <param name="node">The node to move.</param>
        /// <param name="baseNode">The same node, as found on the prefab part.</param>
        /// <param name="movePart">Whether or not to move attached parts.</param>
        /// <param name="absolute">Whether to use absolute or relative scaling.</param>
        private void MoveNode(ScaleChange scaleChange, AttachNode node, AttachNode baseNode, bool movePart, bool absolute)
        {
            if (baseNode == null)
            {
                baseNode = node;
                absolute = false;
            }

            Vector3 oldPosition = node.position;

            if (absolute)
            {
                node.position = baseNode.position * scaleChange.scale;
                node.originalPosition = baseNode.originalPosition * scaleChange.scale;
            }
            else
            {
                node.position = node.position * scaleChange.relativeScale;
                node.originalPosition = node.originalPosition * scaleChange.relativeScale;
            }

            if (movePart)
                MovePart(scaleChange, node.attachedPart, node.position, oldPosition);

            ScaleAttachNode(node, baseNode);
        }

        private void MovePart(ScaleChange scaleChange, Part attachedPart, Vector3 newPosition, Vector3 oldPosition)
        {
            if (attachedPart == null)
                return;

            Vector3 deltaPos = newPosition - oldPosition;

            if (attachedPart == part.parent)
            {
                part.transform.Translate(-deltaPos, part.transform);
            }
            else
            {
                Vector3 offset = attachedPart.attPos * (scaleChange.relativeScale - 1f);
                attachedPart.transform.Translate(deltaPos + offset, part.transform);
                attachedPart.attPos *= scaleChange.relativeScale;
            }
        }

        private void ScaleDragCubes(ScaleChange scaleChange, bool absolute)
        {
            float factor = absolute ? scaleChange.scale : scaleChange.relativeScale;
            if (factor == 1f)
                return;

            float quadraticFactor = factor * factor * factor;

            foreach (DragCube dragCube in part.DragCubes.Cubes)
            {
                dragCube.Size *= factor;

                int i = dragCube.Area.Length;
                while (i-- > 0)
                    dragCube.Area[i] *= quadraticFactor;

                i = dragCube.Depth.Length;
                while (i-- > 0)
                    dragCube.Depth[i] *= factor;
            }

            part.DragCubes.ForceUpdate(true, true);
        }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation situation)
        {
            if (_setupRun && IsScaled)
                return costModifier;
            else
                return 0f;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation)
        {
            if (_setupRun && IsScaled)
                return massModifier;
            else
                return 0f;
        }

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        //void CallUpdaters()
        //{
        //// two passes, to depend less on the order of this list
        //int len = _updaters.Length;
        //for (int i = 0; i < len; i++)
        //{
        //    // first apply the exponents
        //    var updater = _updaters[i];
        //    if (updater is TSGenericUpdater)
        //    {
        //        try
        //        {
        //            float oldMass = part.mass;
        //            updater.OnRescale(ScalingFactor);
        //            part.mass = oldMass; // make sure we leave this in a clean state
        //        }
        //        catch (Exception e)
        //        {
        //            Debug.LogWarning("Exception on rescale: " + e.ToString());
        //        }
        //    }
        //}
        //if (_prefabPart.CrewCapacity > 0)
        //    UpdateCrewManifest();

        //if (part.Modules.Contains("ModuleDataTransmitter"))
        //    UpdateAntennaPowerDisplay();

        //// send scaling part message
        //var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
        //data.Set<float>("factorAbsolute", ScalingFactor.absolute.linear);
        //data.Set<float>("factorRelative", ScalingFactor.relative.linear);
        //part.SendEvent("OnPartScaleChanged", data, 0);

        //len = _updaters.Length;
        //for (int i = 0; i < len; i++)
        //{
        //    var updater = _updaters[i];
        //    // then call other updaters (emitters, other mods)
        //    if (updater is TSGenericUpdater)
        //        continue;

        //    updater.OnRescale(ScalingFactor);
        //}
        //}

        // scale IVA overlay
        //if (HighLogic.LoadedSceneIsFlight && enabled && (part.internalModel != null))
        //{
        //    _savedIvaScale = part.internalModel.transform.localScale * ScalingFactor.absolute.linear;
        //    part.internalModel.transform.localScale = _savedIvaScale;
        //    part.internalModel.transform.hasChanged = true;
        //}

        //void Update()
        //{
        //    if (HighLogic.LoadedSceneIsFlight)
        //    {
        //        // flight scene frequently nukes our OnStart resize some time later
        //        if ((part.internalModel != null) && (part.internalModel.transform.localScale != _savedIvaScale))
        //        {
        //            part.internalModel.transform.localScale = _savedIvaScale;
        //            part.internalModel.transform.hasChanged = true;
        //        }
        //    }

        //    int len = _updaters.Length;
        //    for (int i = 0; i < len; i++)
        //    {
        //        if (_updaters[i] is IUpdateable)
        //            (_updaters[i] as IUpdateable).OnUpdate();
        //    }
        //}

        //private void UpdateCrewManifest()
        //{
        //    if (!HighLogic.LoadedSceneIsEditor) { return; } //only run the following block in the editor; it updates the crew-assignment GUI

        //    VesselCrewManifest vcm = ShipConstruction.ShipManifest;
        //    if (vcm == null) { return; }
        //    PartCrewManifest pcm = vcm.GetPartCrewManifest(part.craftID);
        //    if (pcm == null) { return; }

        //    int len = pcm.partCrew.Length;
        //    int newLen = Math.Min(part.CrewCapacity, _prefabPart.CrewCapacity);
        //    if (len == newLen) { return; }

        //    if (EditorLogic.fetch.editorScreen == EditorScreen.Crew)
        //        EditorLogic.fetch.SelectPanelParts();

        //    for (int i = 0; i < len; i++)
        //        pcm.RemoveCrewFromSeat(i);

        //    pcm.partCrew = new string[newLen];
        //    for (int i = 0; i < newLen; i++)
        //        pcm.partCrew[i] = string.Empty;

        //    ShipConstruction.ShipManifest.SetPartManifest(part.craftID, pcm);
        //}

        //private void UpdateAntennaPowerDisplay()
        //{
        //    var m = part.Modules["ModuleDataTransmitter"] as ModuleDataTransmitter;
        //    double p = m.antennaPower / 1000;
        //    Char suffix = 'k';
        //    if (p >= 1000)
        //    {
        //        p /= 1000f;
        //        suffix = 'M';
        //        if (p >= 1000)
        //        {
        //            p /= 1000;
        //            suffix = 'G';
        //        }
        //    }
        //    p = Math.Round(p, 2);
        //    string str = p.ToString() + suffix;
        //    if (m.antennaCombinable) { str += " (Combinable)"; }
        //    m.powerText = str;
        //}
    }
}
