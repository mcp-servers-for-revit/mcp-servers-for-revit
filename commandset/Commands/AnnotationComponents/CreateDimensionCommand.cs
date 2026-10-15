// 
//                       RevitAPI-Solutions
// Copyright (c) Duong Tran Quang (DTDucas) (baymax.contact@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//

using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Annotation;
using RevitMCPCommandSet.Services.AnnotationComponents;

namespace RevitMCPCommandSet.Commands.AnnotationComponents;

/// <summary>
///     Command to create dimensions
/// </summary>
public class CreateDimensionCommand : RevitCommandBase
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="uiApp">Revit UIApplication</param>
    public CreateDimensionCommand(UIApplication uiApp)
        : base(new CreateDimensionEventHandler(), uiApp)
    {
    }

    private CreateDimensionEventHandler _handler => (CreateDimensionEventHandler)Handler;

    /// <summary>
    ///     Command name
    /// </summary>
    public override string CommandName => "create_dimensions";

    /// <summary>
    ///     Execute dimension creation command
    /// </summary>
    /// <param name="parameters">JSON parameters</param>
    /// <param name="requestId">Request ID</param>
    /// <returns>Execution result</returns>
    public override object Execute(JObject parameters, string requestId)
    {
        try
        {
            // Parse parameters
            var dimensions = parameters["dimensions"]?.ToObject<List<DimensionCreationInfo>>();

            if (dimensions == null || dimensions.Count == 0)
                throw new ArgumentException("Dimension list cannot be empty");

            // Set parameters and execute
            _handler.SetParameters(dimensions);

            // Raise event and wait for completion
            if (RaiseAndWaitForCompletion(20000)) // 20 seconds timeout
                return _handler.Result;
            throw new TimeoutException("Dimension creation operation timed out");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error creating dimensions: {ex.Message}", ex);
        }
    }
}