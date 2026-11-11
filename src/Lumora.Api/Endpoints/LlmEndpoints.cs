using Lumora.Application.Abstractions.Llm;
using Lumora.Contracts.Llm;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lumora.Api.Endpoints;

internal static class LlmEndpoints
{
    public static WebApplication MapLlmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/llm").WithTags("Local LLM");
        group.MapGet("/status", GetStatusAsync)
            .WithName("GetLlmProviderStatus")
            .WithSummary("로컬 LLM Provider 상태 확인")
            .WithDescription("상태를 매 요청마다 실시간 조회합니다. 이전 성공 상태를 캐시해 사용하지 않습니다.")
            .Produces<LlmProviderStatusResponse>();
        group.MapGet("/version", GetVersionAsync)
            .WithName("GetLlmProviderVersion")
            .WithSummary("로컬 Ollama 버전 조회")
            .Produces<LlmVersionResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/models", GetModelsAsync)
            .WithName("GetLocalLlmModels")
            .WithSummary("로컬에 설치된 모델 목록 조회")
            .WithDescription("설치된 모델의 이름, ID, 크기, 수정 시각 및 Ollama가 제공한 세부 정보를 반환합니다.")
            .Produces<LlmModelListResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/models/{modelName}", GetModelAvailabilityAsync)
            .WithName("GetLocalLlmModelAvailability")
            .WithSummary("로컬 모델 설치 상태 확인")
            .WithDescription("설치 여부만 확인하며 실제 추론 성공 여부는 검사하지 않습니다.")
            .Produces<LlmModelAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        return app;
    }

    private static async Task<Ok<LlmProviderStatusResponse>> GetStatusAsync(
        ILlmProvider provider,
        CancellationToken cancellationToken)
    {
        var result = await provider.GetStatusAsync(cancellationToken);
        return TypedResults.Ok(new LlmProviderStatusResponse(
            result.ProviderName,
            result.ConnectionState.ToString(),
            result.RuntimeVersion,
            result.InstalledModelCount,
            result.CheckedAtUtc,
            result.ErrorKind?.ToString()));
    }

    private static async Task<Results<Ok<LlmVersionResponse>, ProblemHttpResult>> GetVersionAsync(
        ILlmProvider provider,
        CancellationToken cancellationToken)
    {
        var version = await provider.GetVersionAsync(cancellationToken);
        return TypedResults.Ok(new LlmVersionResponse(provider.Name, version));
    }

    private static async Task<Results<Ok<LlmModelListResponse>, ProblemHttpResult>> GetModelsAsync(
        ILlmProvider provider,
        CancellationToken cancellationToken)
    {
        var models = await provider.GetModelsAsync(cancellationToken);
        return TypedResults.Ok(new LlmModelListResponse(models.Select(ToResponse).ToArray()));
    }

    private static async Task<Results<Ok<LlmModelAvailabilityResponse>, ProblemHttpResult>> GetModelAvailabilityAsync(
        string modelName,
        ILlmProvider provider,
        CancellationToken cancellationToken)
    {
        var availability = await provider.GetModelAvailabilityAsync(modelName, cancellationToken);
        return TypedResults.Ok(new LlmModelAvailabilityResponse(
            availability.ModelName,
            availability.State.ToString(),
            availability.Model is null ? null : ToResponse(availability.Model),
            availability.ErrorKind?.ToString()));
    }

    private static LlmModelResponse ToResponse(LlmModelInfo model) =>
        new(
            model.Name,
            model.Identifier,
            model.Digest,
            model.SizeBytes,
            model.ModifiedAtUtc,
            model.Format,
            model.Family,
            model.Families,
            model.ParameterSize,
            model.QuantizationLevel);
}
