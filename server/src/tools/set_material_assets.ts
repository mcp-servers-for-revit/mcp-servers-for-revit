import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerSetMaterialAssetsTool(server: McpServer) {
  server.tool(
    "set_material_assets",
    "Modify the structural and/or thermal asset properties of a material. Values are in Revit internal units (metric: kg, m, Pa, W, etc.).",
    {
      materialId: z
        .number()
        .optional()
        .describe("Material element ID. Provide this OR materialName."),
      materialName: z
        .string()
        .optional()
        .describe("Material name (case-insensitive)."),
      // Structural properties
      density: z
        .number()
        .optional()
        .describe("Structural density (kg/m³)"),
      youngModulus: z
        .number()
        .optional()
        .describe("Young's modulus (Pa). Sets X, Y, Z to the same value."),
      poissonRatio: z
        .number()
        .optional()
        .describe("Poisson's ratio (dimensionless). Sets X, Y, Z to the same value."),
      shearModulus: z
        .number()
        .optional()
        .describe("Shear modulus (Pa). Sets X, Y, Z to the same value."),
      thermalExpansionCoefficient: z
        .number()
        .optional()
        .describe("Thermal expansion coefficient (1/°C). Sets X, Y, Z to the same value."),
      minimumYieldStress: z
        .number()
        .optional()
        .describe("Minimum yield stress (Pa)"),
      minimumTensileStrength: z
        .number()
        .optional()
        .describe("Minimum tensile strength (Pa)"),
      behavior: z
        .enum(["Isotropic", "Orthotropic", "TransverselyIsotropic"])
        .optional()
        .describe("Structural behavior type"),
      // Thermal properties
      thermalConductivity: z
        .number()
        .optional()
        .describe("Thermal conductivity (W/(m·K))"),
      specificHeat: z
        .number()
        .optional()
        .describe("Specific heat (J/(kg·K))"),
      thermalDensity: z
        .number()
        .optional()
        .describe("Thermal density (kg/m³). Separate from structural density."),
      emissivity: z
        .number()
        .min(0)
        .max(1)
        .optional()
        .describe("Emissivity (0.0-1.0)"),
      permeability: z
        .number()
        .optional()
        .describe("Permeability (ng/(Pa·s·m²))"),
      porosity: z
        .number()
        .min(0)
        .max(1)
        .optional()
        .describe("Porosity (0.0-1.0)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_material_assets", args);
        }, 120000);

        return toolResponse("set_material_assets", response);
      } catch (error) {
        return toolError(
          "set_material_assets",
          `Set material assets failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
