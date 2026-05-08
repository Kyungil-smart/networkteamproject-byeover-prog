using System.Collections.Generic;
using UnityEngine;


namespace DeadZone.Core
{
    [CreateAssetMenu(menuName = "DeadZone/Housing/Recipe", fileName = "Recipe_New")]
    public class RecipeSO : ScriptableObject
    {
        public string recipeID;
        public ItemDataSO result;
        public int resultCount = 1;
        public List<ItemRequirement> ingredients;
        public int requiredFacilityLevel = 1;
        public RarityTier requiredTier = RarityTier.Common;
    }
}