using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services
{
    public class CreateMaterialEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string MaterialName { get; set; }
        public string DuplicateFrom { get; set; }
        public int? ColorR { get; set; }
        public int? ColorG { get; set; }
        public int? ColorB { get; set; }
        public int? Transparency { get; set; }
        public int? Shininess { get; set; }
        public int? Smoothness { get; set; }
        public string MaterialClassName { get; set; }
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                Material material = null;

                using (var transaction = new Transaction(doc, "Create Material"))
                {
                    transaction.Start();
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(DuplicateFrom))
                        {
                            // Find existing material by name
                            var existingMat = new FilteredElementCollector(doc)
                                .OfClass(typeof(Material))
                                .Cast<Material>()
                                .FirstOrDefault(m => string.Equals(m.Name, DuplicateFrom, StringComparison.OrdinalIgnoreCase));

                            if (existingMat == null)
                            {
                                transaction.RollBack();
                                Result = new AIResult<object>
                                {
                                    Success = false,
                                    Message = $"Source material '{DuplicateFrom}' not found"
                                };
                                return;
                            }

                            material = existingMat.Duplicate(MaterialName) as Material;
                        }
                        else
                        {
                            var newId = Material.Create(doc, MaterialName);
                            material = doc.GetElement(newId) as Material;
                        }

                        if (material == null)
                        {
                            transaction.RollBack();
                            Result = new AIResult<object>
                            {
                                Success = false,
                                Message = "Failed to create material"
                            };
                            return;
                        }

                        // Set optional properties if provided
                        if (ColorR.HasValue && ColorG.HasValue && ColorB.HasValue)
                        {
                            material.Color = new Color((byte)ColorR.Value, (byte)ColorG.Value, (byte)ColorB.Value);
                        }

                        if (Transparency.HasValue)
                        {
                            material.Transparency = Transparency.Value;
                        }

                        if (Shininess.HasValue)
                        {
                            material.Shininess = Shininess.Value;
                        }

                        if (Smoothness.HasValue)
                        {
                            material.Smoothness = Smoothness.Value;
                        }

                        if (!string.IsNullOrWhiteSpace(MaterialClassName))
                        {
                            material.MaterialClass = MaterialClassName;
                        }

                        transaction.Commit();

                        // Build response
                        string colorHex = null;
                        if (material.Color.IsValid)
                        {
                            colorHex = $"#{material.Color.Red:X2}{material.Color.Green:X2}{material.Color.Blue:X2}";
                        }

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = string.IsNullOrWhiteSpace(DuplicateFrom)
                                ? $"Material '{MaterialName}' created successfully"
                                : $"Material '{MaterialName}' duplicated from '{DuplicateFrom}' successfully",
                            Response = new
                            {
#if REVIT2024_OR_GREATER
                                id = material.Id.Value,
#else
                                id = material.Id.IntegerValue,
#endif
                                name = material.Name,
                                color = colorHex,
                                transparency = material.Transparency,
                                shininess = material.Shininess,
                                smoothness = material.Smoothness,
                                materialClass = material.MaterialClass
                            }
                        };
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to create material: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Create Material";
    }
}
