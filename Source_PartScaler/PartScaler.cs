using System;
using System.Collections.Generic;
using System.Linq;
using Smooth.Slinq;
using UnityEngine;

namespace PartScaler
{
    public class PartScaler : PartModule, IPartCostModifier, IPartMassModifier
    {
        /// <summary>
        /// The selected scale. Different from currentScale only for destination single update, where currentScale is set to match this.
        /// </summary>
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Scale", guiFormat = "0.000", guiUnits = "m")]
        [UI_ScaleEdit(scene = UI_Scene.Editor)]
        public float tweakScale = -1;

        /// <summary>
        /// Index into scale values array.
        /// </summary>
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Scale")]
        [UI_ChooseOption(scene = UI_Scene.Editor)]
        public int tweakName = 0;

        /// <summary>
        /// The scale to which the part currently is scaled.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float currentScale = -1;

        /// <summary>
        /// The default scale, i.e. the number by which to divide tweakScale and currentScale to get the relative size difference from when the part is used without TweakScale.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float defaultScale = -1;

        /// <summary>
        /// Whether the part should be freely scalable or limited to destination list of allowed values.
        /// </summary>
        [KSPField(isPersistant = false)]
        public bool isFreeScale = false;

        /// <summary>
        /// The scale exponentValue array. If isFreeScale is false, the part may only be one of these scales.
        /// </summary>
        protected float[] ScaleFactors = { 0.625f, 1.25f, 2.5f, 3.75f, 5f };
        
        /// <summary>
        /// The node scale array. If node scales are defined the nodes will be resized to these values.
        ///</summary>
        protected int[] ScaleNodes = Array.Empty<int>();

        /// <summary>
        /// The unmodified prefab part. From this, default values are found.
        /// </summary>
        private Part _prefabPart;

        /// <summary>
        /// Cached scale vector, we need this because the game regularly reverts the scaling of the IVA overlay
        /// </summary>
        private Vector3 _savedIvaScale;

        /// <summary>
        /// The exponentValue by which the part is scaled by default. When destination part uses MODEL { scale = ... }, this will be different from (1,1,1).
        /// </summary>
        [KSPField(isPersistant = true)]
        public Vector3 defaultTransformScale = new Vector3(0f, 0f, 0f);

        private bool _firstUpdateWithParent = true;
        private bool _setupRun;
        private bool _firstUpdate = true;
        public bool ignoreResourcesForCost = false;
        public bool scaleMass = true;

        public bool SetupRun => _setupRun;

        /// <summary>
        /// Updaters for different PartModules.
        /// </summary>
        private IRescalable[] _updaters = new IRescalable[0];

        /// <summary>
        /// Cost of unscaled, empty part.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float DryCost;

        /// <summary>
        /// scaled mass
        /// </summary>
        [KSPField(isPersistant = false)]
        public float MassScale = 1;

        /// <summary>
        /// The ScaleType for this part.
        /// </summary>
        public ScaleType ScaleType { get; private set; }

        public bool IsRescaled
        {
            get
            {
                return (Math.Abs(currentScale / defaultScale - 1f) > 1e-5f);
            }
        }

        /// <summary>
        /// The current scaling factor.
        /// </summary>
        public ScalingFactor ScalingFactor
        {
            get
            {
                return new ScalingFactor(tweakScale / defaultScale, tweakScale / currentScale, isFreeScale ? -1 : tweakName);
            }
        }


        protected virtual void SetupPrefab()
        {
            ConfigNode moduleNode = null;
            foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.GetConfigs("PART"))
            {
                if (urlConfig.name.Replace('_', '.') != part.name)
                    continue;

                foreach (ConfigNode node in urlConfig.config.nodes)
                {
                    if (node.name == "MODULE" && node.GetValue("name") == moduleName)
                    {
                        moduleNode = node;
                        break;
                    }
                }
            }

            if (moduleNode == null)
            {
                Debug.LogError($"[PartScaler] Couldn't find module ConfigNode in part {part.name}");
                return;
            }

            ScaleType = new ScaleType(moduleNode);
            SetupFromConfig(ScaleType);
            tweakScale = currentScale = defaultScale;
        }

        /// <summary>
        /// Sets up values from ScaleType, creates updaters, and sets up initial values.
        /// </summary>
        protected virtual void Setup()
        {
            if (_setupRun)
            {
                return;
            }
            _prefabPart = part.partInfo.partPrefab;
            _updaters = TweakScaleUpdater.CreateUpdaters(part).ToArray();

            ScaleType = _prefabPart.FindModuleImplementing<PartScaler>().ScaleType;
            SetupFromConfig(ScaleType);

            if (!isFreeScale && ScaleFactors.Length != 0)
            {
                tweakName = Tools.ClosestIndex(tweakScale, ScaleFactors);
                tweakScale = ScaleFactors[tweakName];
            }

            if (IsRescaled)
            {
                ScalePart(false, true);
                try
                {
                    CallUpdaters();
                }
                catch (Exception exception)
                {
                    Tools.LogWf("Exception on Rescale: {0}", exception);
                }
            }
            else
            {
                DryCost = part.partInfo.cost;
                foreach (PartResource resource in part.Resources)
                    DryCost += (float)(resource.maxAmount * resource.info.unitCost);

                if (DryCost < 0)
                {
                    Debug.LogError("TweakScale: part=" + part.name + ", DryCost=" + DryCost.ToString());
                    DryCost = 0;
                }
            }
            _setupRun = true;
        }

        /// <summary>
        /// Loads settings from <paramref name="scaleType"/>.
        /// </summary>
        /// <param name="scaleType">The settings to use.</param>
        private void SetupFromConfig(ScaleType scaleType)
        {
            if (ScaleType == null) Debug.LogError("TweakScale: Scaletype==null! part=" + part.name);

            isFreeScale = scaleType.IsFreeScale;
            if (defaultScale == -1)
                defaultScale = scaleType.DefaultScale;

            if (currentScale == -1)
                currentScale = defaultScale;
            else if (defaultScale != scaleType.DefaultScale)
            {
                Tools.Logf("defaultScale has changed for part {0}: keeping relative scale.", part.name);
                currentScale *= scaleType.DefaultScale / defaultScale;
                defaultScale = scaleType.DefaultScale;
            }

            if (tweakScale == -1)
                tweakScale = currentScale;
            Fields["tweakScale"].guiActiveEditor = false;
            Fields["tweakName"].guiActiveEditor = false;
            ScaleFactors = scaleType.ScaleFactors;
            if (ScaleFactors.Length <= 0)
                return;

            if (isFreeScale)
            {
                Fields["tweakScale"].guiActiveEditor = true;
                var range = (UI_ScaleEdit)Fields["tweakScale"].uiControlEditor;
                range.intervals = scaleType.ScaleFactors;
                range.incrementSlide = scaleType.IncrementSlide;
                range.unit = scaleType.Suffix;
                range.sigFigs = 3;
                Fields["tweakScale"].guiUnits = scaleType.Suffix;
            }
            else
            {
                Fields["tweakName"].guiActiveEditor = scaleType.ScaleFactors.Length > 1;
                var options = (UI_ChooseOption)Fields["tweakName"].uiControlEditor;
                ScaleNodes = scaleType.ScaleNodes;
                options.options = scaleType.ScaleNames;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                // Loading of the prefab from the part config
                _prefabPart = part;
                SetupPrefab();
            }
            else
            {
                // Loading of the part from a saved craft
                tweakScale = currentScale;
                if (HighLogic.LoadedSceneIsEditor || IsRescaled)
                    Setup();
                else
                    enabled = false;
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsEditor)
            {
                if (part.parent != null)
                {
                    _firstUpdateWithParent = false;
                }
                Setup();

                if (_prefabPart.CrewCapacity > 0)
                {
                    GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                }
            }

            // scale IVA overlay
            if (HighLogic.LoadedSceneIsFlight && enabled && (part.internalModel != null))
            {
                _savedIvaScale = part.internalModel.transform.localScale * ScalingFactor.absolute.linear;
                part.internalModel.transform.localScale = _savedIvaScale;
                part.internalModel.transform.hasChanged = true;
            }
        }

        /// <summary>
        /// Scale has changed!
        /// </summary>
        public void OnTweakScaleChanged()
        {
            if (!isFreeScale)
            {
                tweakScale = ScaleFactors[tweakName];
            }

            ScalePart(true, false);
            ScaleDragCubes(false);
            MarkWindowDirty();
            CallUpdaters();

            currentScale = tweakScale;
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        void OnEditorShipModified(ShipConstruct ship)
        {
            if (part.CrewCapacity >= _prefabPart.CrewCapacity) { return; }

            UpdateCrewManifest();
        }

        void Update()
        {
            if (_firstUpdate)
            {
                _firstUpdate = false;
                if (CheckIntegrity())
                    return;

                if (IsRescaled)
                {
                    ScaleDragCubes(true);
                    if (HighLogic.LoadedSceneIsEditor)
                        ScalePart(false, true);  // cloned parts and loaded crafts seem to need this (otherwise the node positions revert)
                }
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                if (currentScale >= 0f)
                {
                    var changed = currentScale != (isFreeScale ? tweakScale : ScaleFactors[tweakName]);
                    if (changed) // user has changed the scale tweakable
                    {
                        // If the user has changed the scale of the part before attaching it, we want to keep that scale.
                        _firstUpdateWithParent = false;
                        OnTweakScaleChanged();
                    }
                }
            }
            else
            {
                // flight scene frequently nukes our OnStart resize some time later
                if ((part.internalModel != null) && (part.internalModel.transform.localScale != _savedIvaScale))
                {
                    part.internalModel.transform.localScale = _savedIvaScale;
                    part.internalModel.transform.hasChanged = true;
                }
            }

            if (_firstUpdateWithParent && part.HasParent())
            {
                _firstUpdateWithParent = false;
            }

            int len = _updaters.Length;
            for (int i = 0; i < len; i++)
            {
                if (_updaters[i] is IUpdateable)
                    (_updaters[i] as IUpdateable).OnUpdate();
            }
        }

        void CallUpdaters()
        {
            // two passes, to depend less on the order of this list
            int len = _updaters.Length;
            for (int i = 0; i < len; i++)
            {
                // first apply the exponents
                var updater = _updaters[i];
                if (updater is TSGenericUpdater)
                {
                    try
                    {
                        float oldMass = part.mass;
                        updater.OnRescale(ScalingFactor);
                        part.mass = oldMass; // make sure we leave this in a clean state
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("Exception on rescale: " + e.ToString());
                    }
                }
            }
            if (_prefabPart.CrewCapacity > 0)
                UpdateCrewManifest();

            if (part.Modules.Contains("ModuleDataTransmitter"))
                UpdateAntennaPowerDisplay();

            // send scaling part message
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<float>("factorAbsolute", ScalingFactor.absolute.linear);
            data.Set<float>("factorRelative", ScalingFactor.relative.linear);
            part.SendEvent("OnPartScaleChanged", data, 0);

            len = _updaters.Length;
            for (int i = 0; i < len; i++)
            {
                var updater = _updaters[i];
                // then call other updaters (emitters, other mods)
                if (updater is TSGenericUpdater)
                    continue;

                updater.OnRescale(ScalingFactor);
            }
        }

        private void UpdateCrewManifest()
        {
            if (!HighLogic.LoadedSceneIsEditor) { return; } //only run the following block in the editor; it updates the crew-assignment GUI

            VesselCrewManifest vcm = ShipConstruction.ShipManifest;
            if (vcm == null) { return; }
            PartCrewManifest pcm = vcm.GetPartCrewManifest(part.craftID);
            if (pcm == null) { return; }

            int len = pcm.partCrew.Length;
            int newLen = Math.Min(part.CrewCapacity, _prefabPart.CrewCapacity);
            if (len == newLen) { return; }

            if (EditorLogic.fetch.editorScreen == EditorScreen.Crew)
                EditorLogic.fetch.SelectPanelParts();

            for (int i = 0; i < len; i++)
                pcm.RemoveCrewFromSeat(i);

            pcm.partCrew = new string[newLen];
            for (int i = 0; i < newLen; i++)
                pcm.partCrew[i] = string.Empty;

            ShipConstruction.ShipManifest.SetPartManifest(part.craftID, pcm);
        }

        private void UpdateAntennaPowerDisplay()
        {
            var m = part.Modules["ModuleDataTransmitter"] as ModuleDataTransmitter;
            double p = m.antennaPower / 1000;
            Char suffix = 'k';
            if (p >= 1000)
            {
                p /= 1000f;
                suffix = 'M';
                if (p >= 1000)
                {
                    p /= 1000;
                    suffix = 'G';
                }
            }
            p = Math.Round(p, 2);
            string str = p.ToString() + suffix;
            if (m.antennaCombinable) { str += " (Combinable)"; }
            m.powerText = str;
        }

        /// <summary>
        /// Updates properties that change linearly with scale.
        /// </summary>
        /// <param name="moveParts">Whether or not to move attached parts.</param>
        /// <param name="absolute">Whether to use absolute or relative scaling.</param>
        private void ScalePart(bool moveParts, bool absolute)
        {
            ScalePartTransform();
            ScaleVariantsAttachNodes(absolute);
            ScaleAttachNodes(moveParts, absolute);
        }

        private void ScaleAttachNodes(bool moveParts, bool absolute)
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
                                MovePart(attachNode.attachedPart, variantNode.position, attachNode.position);

                            attachNode.position = variantNode.position;
                            attachNode.originalPosition = variantNode.originalPosition;
                            break;
                        }
                    }
                }

                if (nodeIsOnVariant)
                    continue;

                AttachNode pristineNode = null;
                foreach (AttachNode prefabNode in _prefabPart.attachNodes)
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

                MoveNode(attachNode, pristineNode, moveParts, absolute);
            }

            if (part.srfAttachNode != null)
            {
                if (part.variants != null && part.variants.SelectedVariant?.SrfAttachNode != null)
                {
                    if (moveParts)
                        MovePart(part.srfAttachNode.attachedPart, part.variants.SelectedVariant.SrfAttachNode.position, part.srfAttachNode.position);

                    part.srfAttachNode.position = part.variants.SelectedVariant.SrfAttachNode.position;
                    part.srfAttachNode.originalPosition = part.variants.SelectedVariant.SrfAttachNode.originalPosition;
                }
                else
                {
                    MoveNode(part.srfAttachNode, _prefabPart.srfAttachNode, moveParts, absolute);
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
                    var targetPosition = attachedPosition * ScalingFactor.relative.linear;
                    child.transform.Translate(targetPosition - attachedPosition, part.transform);
                }
            }
        }

        private void ScaleVariantsAttachNodes(bool absolute)
        {
            if (part.variants == null)
                return;

            for (int i = 0; i < part.variants.variantList.Count; i++)
            {
                PartVariant partVariant = part.variants.variantList[i];
                PartVariant prefabVariant = _prefabPart.variants.variantList[i];

                for (int j = 0; j < partVariant.AttachNodes.Count; j++)
                {
                    MoveNode(partVariant.AttachNodes[j], prefabVariant.AttachNodes[j], false, absolute);
                }

                if (partVariant.SrfAttachNode != null)
                {
                    MoveNode(partVariant.SrfAttachNode, prefabVariant.SrfAttachNode, false, absolute);
                }
            }
        }

        private void ScalePartTransform()
        {
            part.rescaleFactor = _prefabPart.rescaleFactor * ScalingFactor.absolute.linear;

            var trafo = part.partTransform.Find("model");
            if (trafo != null)
            {
                if (defaultTransformScale.x == 0.0f)
                {
                    defaultTransformScale = trafo.localScale;
                }

                // check for flipped signs
                if (defaultTransformScale.x * trafo.localScale.x < 0)
                {
                    defaultTransformScale.x *= -1;
                }
                if (defaultTransformScale.y * trafo.localScale.y < 0)
                {
                    defaultTransformScale.y *= -1;
                }
                if (defaultTransformScale.z * trafo.localScale.z < 0)
                {
                    defaultTransformScale.z *= -1;
                }

                trafo.localScale = ScalingFactor.absolute.linear * defaultTransformScale;
                trafo.hasChanged = true;
                part.partTransform.hasChanged = true;
            }
        }

        /// <summary>
        /// Change the size of <paramref name="node"/> to reflect the new size of the part it's attached to.
        /// </summary>
        /// <param name="node">The node to resize.</param>
        /// <param name="baseNode">The same node, as found on the prefab part.</param>
        private void ScaleAttachNode(AttachNode node, AttachNode baseNode)
        {
            if (isFreeScale || ScaleNodes == null || ScaleNodes.Length == 0)
            {
                float tmpBaseNodeSize = baseNode.size;
                if (tmpBaseNodeSize == 0)
                {
                    tmpBaseNodeSize = 0.5f;
                }
                node.size = (int)(tmpBaseNodeSize * tweakScale / defaultScale + 0.49);
            }
            else
            {
                node.size = baseNode.size + (1 * ScaleNodes[tweakName]);
            }
            if (node.size < 0)
            {
                node.size = 0;
            }
        }

        private void ScaleDragCubes(bool absolute)
        {
            ScalingFactor.FactorSet factor;
            if (absolute)
                factor = ScalingFactor.absolute;
            else
                factor = ScalingFactor.relative;

            if (factor.linear == 1)
                return;

            int len = part.DragCubes.Cubes.Count;
            for (int ic = 0; ic < len; ic++)
            {
                DragCube dragCube = part.DragCubes.Cubes[ic];
                dragCube.Size *= factor.linear;
                for (int i = 0; i < dragCube.Area.Length; i++)
                    dragCube.Area[i] *= factor.quadratic;

                for (int i = 0; i < dragCube.Depth.Length; i++)
                    dragCube.Depth[i] *= factor.linear;
            }
            part.DragCubes.ForceUpdate(true, true);
        }

        /// <summary>
        /// Moves <paramref name="node"/> to reflect the new scale. If <paramref name="movePart"/> is true, also moves attached parts.
        /// </summary>
        /// <param name="node">The node to move.</param>
        /// <param name="baseNode">The same node, as found on the prefab part.</param>
        /// <param name="movePart">Whether or not to move attached parts.</param>
        /// <param name="absolute">Whether to use absolute or relative scaling.</param>
        public void MoveNode(AttachNode node, AttachNode baseNode, bool movePart, bool absolute)
        {
            if (baseNode == null)
            {
                baseNode = node;
                absolute = false;
            }

            Vector3 oldPosition = node.position;

            if (absolute)
            {
                node.position = baseNode.position * ScalingFactor.absolute.linear;
                node.originalPosition = baseNode.originalPosition * ScalingFactor.absolute.linear;
            }
            else
            {
                node.position = node.position * ScalingFactor.relative.linear;
                node.originalPosition = node.originalPosition * ScalingFactor.relative.linear;
            }

            if (movePart)
                MovePart(node.attachedPart, node.position, oldPosition);

            ScaleAttachNode(node, baseNode);
        }

        private void MovePart(Part attachedPart, Vector3 newPosition, Vector3 oldPosition)
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
                var offset = attachedPart.attPos * (ScalingFactor.relative.linear - 1);
                attachedPart.transform.Translate(deltaPos + offset, part.transform);
                attachedPart.attPos *= ScalingFactor.relative.linear;
            }

        }

        /// <summary>
        /// Propagate relative scaling factor to children.
        /// </summary>
        private void ChainScale()
        {
            int len = part.children.Count;
            for (int i=0; i< len; i++)
            {
                var child = part.children[i];
                var b = child.GetComponent<PartScaler>();
                if (b == null)
                    continue;

                float factor = ScalingFactor.relative.linear;
                if (Math.Abs(factor - 1) <= 1e-4f)
                    continue;

                b.tweakScale *= factor;
                if (!b.isFreeScale && (b.ScaleFactors.Length > 0))
                {
                    b.tweakName = Tools.ClosestIndex(b.tweakScale, b.ScaleFactors);
                }
                b.OnTweakScaleChanged();
            }
        }

        /// <summary>
        /// Disable TweakScale module if something is wrong.
        /// </summary>
        /// <returns>True if something is wrong, false otherwise.</returns>
        private bool CheckIntegrity()
        {
            if (ScaleFactors.Length == 0)
            {
                enabled = false; // disable TweakScale module
                Tools.LogWf("{0}({1}) has no valid scale factors. This is probably caused by an invalid TweakScale configuration for the part.", part.name, part.partInfo.title);
                Debug.Log("[TweakScale]" + this.ToString());
                Debug.Log("[TweakScale]" + ScaleType.ToString());
                return true;
            }
            if (this != part.GetComponent<PartScaler>())
            {
                enabled = false; // disable TweakScale module
                Tools.LogWf("Duplicate TweakScale module on part [{0}] {1}", part.partInfo.name, part.partInfo.title);
                Fields["tweakScale"].guiActiveEditor = false;
                Fields["tweakName"].guiActiveEditor = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Marks the right-click window as dirty (i.e. tells it to update).
        /// </summary>
        private void MarkWindowDirty() // redraw the right-click window with the updated stats
        {
            if (part.PartActionWindow != null && part.PartActionWindow.isActiveAndEnabled)
                part.PartActionWindow.displayDirty = true;
        }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation situation)
        {
            if (_setupRun && IsRescaled)
                if (ignoreResourcesForCost)
                {
                    return (DryCost - part.partInfo.cost);
                }
                else
                {
                    double cost = DryCost - part.partInfo.cost;
                    foreach (PartResource resource in part.Resources)
                        cost += resource.maxAmount * resource.info.unitCost;

                    return (float) cost;
                }
            else
              return 0;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation)
        {
            if (_setupRun && IsRescaled && scaleMass)
              return _prefabPart.mass * (MassScale - 1f);
            else
              return 0;
        }

        public ModifierChangeWhen GetModuleMassChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }


        /// <summary>
        /// These are meant for use with an unloaded part (so you only have the persistent data
        /// but the part is not alive). In this case get currentScale/defaultScale and call
        /// this method on the prefab part.
        /// </summary>
        public double getMassFactor(double rescaleFactor)
        {
            var exponent = ScaleExponents.getMassExponent(ScaleType.Exponents);
            return Math.Pow(rescaleFactor, exponent);
        }
        public double getDryCostFactor(double rescaleFactor)
        {
            var exponent = ScaleExponents.getDryCostExponent(ScaleType.Exponents);
            return Math.Pow(rescaleFactor, exponent);
        }
        public double getVolumeFactor(double rescaleFactor)
        {
            return Math.Pow(rescaleFactor, 3);
        }


        public override string ToString()
        {
            var result = "TweakScale{\n";
            result += "\n _setupRun = " + _setupRun;
            result += "\n isFreeScale = " + isFreeScale;
            result += "\n " + ScaleFactors.Length  + " scaleFactors = ";
            foreach (var s in ScaleFactors)
                result += s + "  ";
            result += "\n tweakScale = "   + tweakScale;
            result += "\n currentScale = " + currentScale;
            result += "\n defaultScale = " + defaultScale;
            //result += " scaleNodes = " + ScaleNodes + "\n";
            //result += "   minValue = " + MinValue + "\n";
            //result += "   maxValue = " + MaxValue + "\n";
            return result + "\n}";
        }

        /*[KSPEvent(guiActive = false, active = true)]
        void OnPartScaleChanged(BaseEventData data)
        {
            float factorAbsolute = data.Get<float>("factorAbsolute");
            float factorRelative = data.Get<float>("factorRelative");
            Debug.Log("PartMessage: OnPartScaleChanged:"
                + "\npart=" + part.name
                + "\nfactorRelative=" + factorRelative.ToString()
                + "\nfactorAbsolute=" + factorAbsolute.ToString());

        }*/

        /*[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Debug")]
        public void debugOutput()
        {
            //var ap = part.partInfo;
            //Debug.Log("prefabCost=" + ap.cost + ", dryCost=" + DryCost +", prefabDryCost=" +(_prefabPart.Modules["TweakScale"] as TweakScale).DryCost);
            //Debug.Log("kisVolOvr=" +part.Modules["ModuleKISItem"].Fields["volumeOverride"].GetValue(part.Modules["ModuleKISItem"]));
            //Debug.Log("ResourceCost=" + (part.Resources.Cast<PartResource>().Aggregate(0.0, (a, b) => a + b.maxAmount * b.info.unitCost) ));

            //Debug.Log("massFactor=" + (part.partInfo.partPrefab.Modules["TweakScale"] as TweakScale).getMassFactor( (double)(currentScale / defaultScale)));
            //Debug.Log("costFactor=" + (part.partInfo.partPrefab.Modules["TweakScale"] as TweakScale).getDryCostFactor( (double)(currentScale / defaultScale)));
            //Debug.Log("volFactor =" + (part.partInfo.partPrefab.Modules["TweakScale"] as TweakScale).getVolumeFactor( (double)(currentScale / defaultScale)));

            //var x = part.collider;
            //Debug.Log("C: " +x.name +", enabled="+x.enabled);
            if (part.Modules.Contains("ModuleRCSFX")) {
                Debug.Log("RCS power=" +(part.Modules["ModuleRCSFX"] as ModuleRCSFX).thrusterPower);
            }
            if (part.Modules.Contains("ModuleEnginesFX"))
            {
                Debug.Log("Engine thrust=" +(part.Modules["ModuleEnginesFX"] as ModuleEnginesFX).maxThrust);
            }
        }*/
    }
}
