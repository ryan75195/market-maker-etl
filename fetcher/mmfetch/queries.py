from __future__ import annotations

ITEM_QUERY = """query itemDetail($id: String!) { item(id: $id) { __typename id status name price lastSoldAt description discountRatio numLikes created updated originalPrice categoryTitle
 photos { imageUrl thumbnail }
 seller { __typename id created numSellItems numSales name isProSeller ratings { count average } }
 itemCondition { __typename id name } itemSize { __typename id name }
 itemCategory { __typename id level name } itemCategoryHierarchy { __typename id level name }
 brand { __typename id name } shippingPayer { __typename id name code }
 shippingFromArea { __typename id name } shippingClass { __typename id fee }
 additionalAttributes { __typename text } } }"""

SEARCH_QUERY = """query webSearch($criteria: SearchInput!) {
  search(criteria: $criteria) {
    count
    itemsList {
      id
      name
      price
      status
      originalPrice
      categoryId
      color { name }
      photos { imageUrl }
      itemCondition { id name }
      brand { id name }
      itemSize { name }
      itemCategory { name }
      itemCategoryHierarchy { id level name }
      seller { sellerId: id }
      shippingPayer { code }
      customFacetsList { facetName value }
    }
  }
}"""
