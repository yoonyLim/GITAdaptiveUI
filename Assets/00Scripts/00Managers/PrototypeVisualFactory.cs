using UnityEngine;

public static class PrototypeVisualFactory
{
    private static Sprite squareSprite;
    private static Sprite circleSprite;

    public static Sprite SquareSprite
    {
        get
        {
            if (squareSprite == null)
            {
                squareSprite = CreateSolidSprite("Prototype Square", 8, 8, false);
            }

            return squareSprite;
        }
    }

    public static Sprite CircleSprite
    {
        get
        {
            if (circleSprite == null)
            {
                circleSprite = CreateSolidSprite("Prototype Circle", 64, 64, true);
            }

            return circleSprite;
        }
    }

    public static SpriteRenderer EnsureSpriteRenderer(
        GameObject target,
        Sprite sprite,
        Color color,
        Vector2 worldSize,
        int sortingOrder)
    {
        SpriteRenderer spriteRenderer = target.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = target.AddComponent<SpriteRenderer>();
        }

        spriteRenderer.sprite = sprite;
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = sortingOrder;
        target.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
        return spriteRenderer;
    }

    public static SpriteRenderer CreateChildSprite(
        string name,
        Transform parent,
        Sprite sprite,
        Color color,
        Vector2 localSize,
        Vector2 localPosition,
        float localRotationZ,
        int sortingOrder)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        child.transform.localRotation = Quaternion.Euler(0f, 0f, localRotationZ);
        return EnsureSpriteRenderer(child, sprite, color, localSize, sortingOrder);
    }

    public static GameObject CreateTelegraphCircle(string name, Vector2 position, float radius, Color color)
    {
        GameObject telegraph = new GameObject(name);
        telegraph.transform.position = new Vector3(position.x, position.y, 0f);
        EnsureSpriteRenderer(telegraph, CircleSprite, color, Vector2.one * radius * 2f, -1);
        return telegraph;
    }

    public static GameObject CreateTelegraphLine(
        string name,
        Vector2 origin,
        Vector2 direction,
        float length,
        float width,
        Color color)
    {
        Vector2 safeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        GameObject telegraph = new GameObject(name);
        telegraph.transform.position = origin + safeDirection * (length * 0.5f);
        telegraph.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(safeDirection.y, safeDirection.x) * Mathf.Rad2Deg);
        EnsureSpriteRenderer(telegraph, SquareSprite, color, new Vector2(length, width), -1);
        return telegraph;
    }

    public static bool PointInLineArea(Vector2 point, Vector2 origin, Vector2 direction, float length, float width)
    {
        Vector2 safeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        Vector2 toPoint = point - origin;
        float forwardDistance = Vector2.Dot(toPoint, safeDirection);

        if (forwardDistance < 0f || forwardDistance > length)
        {
            return false;
        }

        Vector2 closestPoint = origin + safeDirection * forwardDistance;
        return Vector2.Distance(point, closestPoint) <= width * 0.5f;
    }

    private static Sprite CreateSolidSprite(string name, int width, int height, bool circle)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = name + " Texture";
        texture.filterMode = FilterMode.Point;

        Color[] pixels = new Color[width * height];
        Vector2 center = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);
        float radius = Mathf.Min(width, height) * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!circle)
                {
                    pixels[y * width + x] = Color.white;
                    continue;
                }

                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = distance <= radius ? 1f : 0f;
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), width);
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
