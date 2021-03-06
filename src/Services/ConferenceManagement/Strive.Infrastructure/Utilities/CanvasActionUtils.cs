using JsonPatchGenerator;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Strive.Core.Services.WhiteboardService;
using Strive.Core.Services.WhiteboardService.CanvasData;
using Strive.Infrastructure.Serialization;

namespace Strive.Infrastructure.Utilities
{
    public class CanvasActionUtils : ICanvasActionUtils
    {
        public JsonPatchDocument<CanvasObject> CreatePatch(CanvasObject original, CanvasObject modified)
        {
            var patch = JsonPatchFactory.Create(original, modified, JsonConfig.Default,
                JsonPatchFactory.DefaultOptions);

            var typedPatch = new JsonPatchDocument<CanvasObject>();
            foreach (var operation in patch.Operations)
            {
                typedPatch.Operations.Add(new Operation<CanvasObject>(operation.op, operation.path, operation.from,
                    operation.value));
            }

            return typedPatch;
        }
    }
}
