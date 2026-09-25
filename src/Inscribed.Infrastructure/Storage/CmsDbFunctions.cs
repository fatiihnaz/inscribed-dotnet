using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Inscribed.Infrastructure.Storage;

internal static class CmsDbFunctions
{
    public static JsonNode? JsonValue(JsonNode data, string field) =>
        throw new InvalidOperationException($"{nameof(JsonValue)} maps to a PostgreSQL function and can only be used inside an EF Core query.");

    public static string? JsonText(JsonNode data, string? field) =>
        throw new InvalidOperationException($"{nameof(JsonText)} maps to a PostgreSQL function and can only be used inside an EF Core query.");

    public static string? Fold(string? text) =>
        throw new InvalidOperationException($"{nameof(Fold)} maps to a PostgreSQL function and can only be used inside an EF Core query.");

    public static float WordSimilarity(string? query, string? text) =>
        throw new InvalidOperationException($"{nameof(WordSimilarity)} maps to a PostgreSQL function and can only be used inside an EF Core query.");

    public static void Register(ModelBuilder modelBuilder)
    {
        var jsonValue = modelBuilder.HasDbFunction(typeof(CmsDbFunctions).GetMethod(nameof(JsonValue))!);
        jsonValue.HasName("jsonb_extract_path");
        jsonValue.HasStoreType("jsonb");
        jsonValue.HasParameter("data").HasStoreType("jsonb");

        var jsonText = modelBuilder.HasDbFunction(typeof(CmsDbFunctions).GetMethod(nameof(JsonText))!);
        jsonText.HasName("jsonb_extract_path_text");
        jsonText.HasStoreType("text");
        jsonText.HasParameter("data").HasStoreType("jsonb");

        var fold = modelBuilder.HasDbFunction(typeof(CmsDbFunctions).GetMethod(nameof(Fold))!);
        fold.HasTranslation(arguments => TextCall("unaccent", TextCall("lower", arguments[0])));

        var wordSimilarity = modelBuilder.HasDbFunction(typeof(CmsDbFunctions).GetMethod(nameof(WordSimilarity))!);
        wordSimilarity.HasName("word_similarity");
        wordSimilarity.HasStoreType("real");
    }

    private static SqlFunctionExpression TextCall(string name, SqlExpression argument) =>
        new(name, [argument], nullable: true, argumentsPropagateNullability: [true], typeof(string), argument.TypeMapping);
}
