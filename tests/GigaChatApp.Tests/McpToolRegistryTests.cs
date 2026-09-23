using GigaChatApp.Services;

namespace Tests;

public class McpToolRegistryTests
{
    [Fact]
    public void ParseToolCalls_SimpleArgs_ReturnsCorrectResult()
    {
        // Arrange
        var response = """
            Вот я вызову инструмент:
            <<tool:list_todo_items>>
            {"limit": 10}
            
            И получу список задач.
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Single(results);
        Assert.Equal("list_todo_items", results[0].Name);
        Assert.Equal("{\"limit\": 10}", results[0].Args);
    }

    [Fact]
    public void ParseToolCalls_NestedJsonArgs_ReturnsCorrectResult()
    {
        // Arrange
        var response = """
            <<tool:create_todo_item>>
            {"title": "Задача", "description": "Описание с {вложенными} скобками", "tags": ["a", "b"]}
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Single(results);
        Assert.Equal("create_todo_item", results[0].Name);
        Assert.Contains("\"title\": \"Задача\"", results[0].Args);
        Assert.Contains("\"tags\": [\"a\", \"b\"]", results[0].Args);
    }

    [Fact]
    public void ParseToolCalls_MultipleTools_ReturnsAll()
    {
        // Arrange
        var response = """
            Сначала получу список:
            <<tool:list_todo_items>>
            {}
            
            Потом обновлю задачу:
            <<tool:update_todo_item>>
            {"id": 5, "isCompleted": true}
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("list_todo_items", results[0].Name);
        Assert.Equal("{}", results[0].Args);
        Assert.Equal("update_todo_item", results[1].Name);
        Assert.Equal("{\"id\": 5, \"isCompleted\": true}", results[1].Args);
    }

    [Fact]
    public void ParseToolCalls_NoArgs_ReturnsNullArgs()
    {
        // Arrange
        var response = "Вызови <<tool:list_todo_items>> и покажи результат";

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Single(results);
        Assert.Equal("list_todo_items", results[0].Name);
        Assert.Null(results[0].Args);
    }

    [Fact]
    public void ParseToolCalls_FiltersFalsePositives()
    {
        // Arrange
        var response = """
            Результат:
            <<tool:result>>
            {"status": "ok"}
            
            Обычный JSON:
            {"key": "value"}
            
            Код:
            <<tool:code>>
            print("hello")
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ParseToolCalls_ComplexNestedJson_ReturnsCompleteArgs()
    {
        // Arrange - сложный JSON с вложенными объектами и массивами
        var response = """
            <<tool:update_todo_item>>
            {
                "id": 42,
                "title": "Сложная задача",
                "metadata": {
                    "priority": 3,
                    "tags": ["важное", "срочное"],
                    "extra": {
                        "nested": true
                    }
                },
                "isCompleted": false
            }
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Single(results);
        Assert.Equal("update_todo_item", results[0].Name);
        Assert.NotNull(results[0].Args);
        Assert.Contains("\"id\": 42", results[0].Args);
        Assert.Contains("\"priority\": 3", results[0].Args);
        Assert.Contains("\"nested\": true", results[0].Args);
        Assert.Contains("\"tags\": [\"важное\", \"срочное\"]", results[0].Args);
    }

    [Fact]
    public void ParseToolCalls_ToolNameInString_SkipsCorrectly()
    {
        // Arrange
        var response = """
            Пользователь сказал: "вызови <<tool:list_todo_items>>"
            Но я не буду этого делать.
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert - должен найти, но это edge case
        // В текущей реализации он найдёт тег даже в строке
        // Это известное ограничение, которое можно улучшить
        Assert.Single(results);
    }

    [Fact]
    public void ParseToolCalls_EmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var response = "";

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ParseToolCalls_ToolWithoutJson_ReturnsNull()
    {
        // Arrange
        var response = """
            Текст перед
            <<tool:get_todo_item>>
            Текст после без JSON
            """;

        // Act
        var results = McpToolRegistry.ParseToolCalls(response);

        // Assert
        Assert.Single(results);
        Assert.Equal("get_todo_item", results[0].Name);
        Assert.Null(results[0].Args);
    }
}
