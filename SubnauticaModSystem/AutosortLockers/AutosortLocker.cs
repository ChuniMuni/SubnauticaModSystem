﻿using Common.Mod;
using Common.Utility;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace AutosortLockers
{
	public class AutosortLocker : MonoBehaviour
	{
		private static readonly Color MainColor = new Color(1, 0.2f, 0.2f);
		private static readonly Color PulseColor = Color.white;

		private bool initialized;
		private Constructable constructable;
		private StorageContainer container;
		private List<AutosortTarget> singleItemTargets = new List<AutosortTarget>();
		private List<AutosortTarget> categoryTargets = new List<AutosortTarget>();
		private List<AutosortTarget> anyTargets = new List<AutosortTarget>();

		private int sortableItems = 0;
		private int unsortableItems = 0;

		[SerializeField]
		private Image background;
		[SerializeField]
		private Image icon;
		[SerializeField]
		private Text text;
		[SerializeField]
		private Text sortingText;
		[SerializeField]
		private bool isSorting;
		[SerializeField]
		private bool sortedItem;

		public bool IsSorting => isSorting;

		private void Awake()
		{
			constructable = GetComponent<Constructable>();
			container = GetComponent<StorageContainer>();
			container.hoverText = "Open autosorter";
			container.storageLabel = "Autosorter";
		}

		private void Update()
		{
			if (!initialized && constructable._constructed && transform.parent != null)
			{
				Initialize();
			}

			if (!initialized || !constructable._constructed)
			{
				return;
			}

			UpdateText();
		}

		private void UpdateText()
		{
			string output = "";
			if (isSorting)
			{
				output = "Sorting...";
			}
			else if (unsortableItems > 0)
			{
				output = "Unsorted Items: " + unsortableItems;
			}
			else
			{
				output = "Ready to Sort";
			}

			sortingText.text = output;
		}

		private void Initialize()
		{
			background.gameObject.SetActive(true);
			icon.gameObject.SetActive(true);
			text.gameObject.SetActive(true);
			sortingText.gameObject.SetActive(true);

			background.sprite = ImageUtils.LoadSprite(Mod.GetAssetPath("LockerScreen.png"));
			icon.sprite = ImageUtils.LoadSprite(Mod.GetAssetPath("Sorter.png"));

			initialized = true;
		}

		private IEnumerator Start()
		{
			while (true)
			{
				yield return new WaitForSeconds(Mathf.Max(0, Mod.config.SortInterval - (unsortableItems / 60.0f)));
				
				yield return Sort();
			}
		}

		private void AccumulateTargets()
		{
			singleItemTargets.Clear();
			categoryTargets.Clear();
			anyTargets.Clear();

			SubRoot subRoot = gameObject.GetComponentInParent<SubRoot>();
			if (subRoot == null)
			{
				return;
			}

			var allTargets = subRoot.GetComponentsInChildren<AutosortTarget>().ToList();
			foreach (var target in allTargets)
			{
				if (target.isActiveAndEnabled && target.CanAddItems())
				{
					if (target.CanTakeAnyItem())
					{
						anyTargets.Add(target);
					}
					else
					{
						if (target.HasItemFilters())
						{
							singleItemTargets.Add(target);
						}
						if (target.HasCategoryFilters())
						{
							categoryTargets.Add(target);
						}
					}
				}
			}
		}

		private IEnumerator Sort()
		{
			sortedItem = false;
			sortableItems = 0;
			unsortableItems = container.container.count;

			if (!initialized || container.IsEmpty())
			{
				isSorting = false;
				yield break;
			}

			AccumulateTargets();
			if (NoTargets())
			{
				isSorting = false;
				yield break;
			}

			isSorting = true;
			yield return SortFilteredTargets(false);
			if (sortedItem)
			{
				yield break;
			}

			yield return SortFilteredTargets(true);
			if (sortedItem)
			{
				yield break;
			}

			yield return SortAnyTargets();
			if (sortedItem)
			{
				yield break;
			}

			isSorting = false;
		}

		private bool NoTargets()
		{
			return singleItemTargets.Count <= 0 && categoryTargets.Count <= 0 && anyTargets.Count <= 0;
		}

		private IEnumerator SortFilteredTargets(bool byCategory)
		{
			int callsToCanAddItem = 0;
			const int CanAddItemCallThreshold = 10;

			foreach (AutosortTarget target in byCategory ? categoryTargets : singleItemTargets)
			{
				foreach (AutosorterFilter filter in target.GetCurrentFilters())
				{
					if (filter.IsCategory() == byCategory)
					{
						foreach (var techType in filter.Types)
						{
							callsToCanAddItem++;
							var items = container.container.GetItems(techType);
							if (items != null && items.Count > 0 && target.CanAddItem(items[0].item))
							{
								sortableItems += items.Count;
								unsortableItems -= items.Count;
								SortItem(items[0].item, target);
								sortedItem = true;
								yield break;
							}
							else if (callsToCanAddItem > CanAddItemCallThreshold)
							{
								callsToCanAddItem = 0;
								yield return null;
							}
						}
					}
				}
			}
		}

		private IEnumerator SortAnyTargets()
		{
			int callsToCanAddItem = 0;
			const int CanAddItemCallThreshold = 10;
			foreach (var item in container.container.ToList())
			{
				foreach (AutosortTarget target in anyTargets)
				{
					callsToCanAddItem++;
					if (target.CanAddItem(item.item))
					{
						SortItem(item.item, target);
						sortableItems++;
						unsortableItems--;
						sortedItem = true;
						yield break;
					}
					else if (callsToCanAddItem > CanAddItemCallThreshold)
					{
						callsToCanAddItem = 0;
						yield return null;
					}
				}
			}
		}

		private void SortItem(Pickupable pickup, AutosortTarget target)
		{
			container.container.RemoveItem(pickup, true);
			target.AddItem(pickup);
			sortableItems++;

			StartCoroutine(PulseIcon());
		}

		public IEnumerator PulseIcon()
		{
			float t = 0;
			float rate = 0.5f;
			while (t < 1.0)
			{
				t += Time.deltaTime * rate;
				icon.color = Color.Lerp(PulseColor, MainColor, t);
				yield return null;
			}
		}

		private AutosortTarget FindTarget(Pickupable item)
		{
			foreach (AutosortTarget target in singleItemTargets)
			{
				if (target.CanAddItemByItemFilter(item))
				{
					return target;
				}
			}
			foreach (AutosortTarget target in categoryTargets)
			{
				if (target.CanAddItemByCategoryFilter(item))
				{
					return target;
				}
			}
			foreach (AutosortTarget target in anyTargets)
			{
				if (target.CanAddItem(item))
				{
					return target;
				}
			}
			return null;
		}



		///////////////////////////////////////////////////////////////////////////////////////////
		public static void AddBuildable()
		{
			BuilderUtils.AddBuildable(new CustomTechInfo() {
				getPrefab = AutosortLocker.GetPrefab,
				techType = Mod.GetTechType(CustomTechType.AutosortLocker),
				techGroup = TechGroup.InteriorModules,
				techCategory = TechCategory.InteriorModule,
				knownAtStart = true,
				assetPath = "Submarine/Build/AutosortLocker",
				displayString = "Autosorter",
				tooltip = "Small, wall-mounted smart-locker that automatically transfers items into linked Autosort Receptacles.",
				techTypeKey = CustomTechType.AutosortLocker.ToString(),
				sprite = new Atlas.Sprite(ImageUtils.LoadTexture(Mod.GetAssetPath("AutosortLocker.png"))),
				recipe = Mod.config.EasyBuild
				? new List<CustomIngredient> {
					new CustomIngredient() {
						techType = TechType.Titanium,
						amount = 2
					}
				}
				: new List<CustomIngredient>
				{
					new CustomIngredient() {
						techType = TechType.Titanium,
						amount = 2
					},
					new CustomIngredient() {
						techType = TechType.ComputerChip,
						amount = 1
					},
					new CustomIngredient() {
						techType = TechType.AluminumOxide,
						amount = 2
					}
				}
			});
		}

		public static GameObject GetPrefab()
		{
			GameObject originalPrefab = Resources.Load<GameObject>("Submarine/Build/SmallLocker");
			GameObject prefab = GameObject.Instantiate(originalPrefab);

			prefab.name = "Autosorter";

			var container = prefab.GetComponent<StorageContainer>();
			container.width = Mod.config.AutosorterWidth;
			container.height = Mod.config.AutosorterHeight;
			container.container.Resize(Mod.config.AutosorterWidth, Mod.config.AutosorterHeight);

			var meshRenderers = prefab.GetComponentsInChildren<MeshRenderer>();
			foreach (var meshRenderer in meshRenderers)
			{
				meshRenderer.material.color = new Color(1, 0, 0);
			}

			var prefabText = prefab.GetComponentInChildren<Text>();
			var label = prefab.FindChild("Label");
			DestroyImmediate(label);

			var autoSorter = prefab.AddComponent<AutosortLocker>();

			var canvas = LockerPrefabShared.CreateCanvas(prefab.transform);
			autoSorter.background = LockerPrefabShared.CreateBackground(canvas.transform);
			autoSorter.icon = LockerPrefabShared.CreateIcon(autoSorter.background.transform, MainColor, 40);
			autoSorter.text = LockerPrefabShared.CreateText(autoSorter.background.transform, prefabText, MainColor, 0, 14, "Autosorter");

			autoSorter.sortingText = LockerPrefabShared.CreateText(autoSorter.background.transform, prefabText, MainColor, -120, 12, "Sorting...");
			autoSorter.sortingText.alignment = TextAnchor.UpperCenter;

			autoSorter.background.gameObject.SetActive(false);
			autoSorter.icon.gameObject.SetActive(false);
			autoSorter.text.gameObject.SetActive(false);
			autoSorter.sortingText.gameObject.SetActive(false);

			return prefab;
		}
	}
}
