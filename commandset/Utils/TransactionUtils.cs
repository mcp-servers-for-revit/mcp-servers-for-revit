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

namespace RevitMCPCommandSet.Utils;

/// <summary>
///     Utilities for handling transactions in Revit
/// </summary>
public static class TransactionUtils
{
    /// <summary>
    ///     Execute an operation within a transaction
    /// </summary>
    /// <typeparam name="T">Result data type</typeparam>
    /// <param name="doc">Revit document</param>
    /// <param name="transactionName">Transaction name</param>
    /// <param name="action">Action to execute</param>
    /// <returns>Action result</returns>
    public static T ExecuteInTransaction<T>(Document doc, string transactionName, Func<T> action)
    {
        using var transaction = new Transaction(doc, transactionName);
        transaction.Start();
        try
        {
            var result = action();
            transaction.Commit();
            return result;
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            throw new Exception($"Error executing '{transactionName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Execute an operation within a transaction (no return value)
    /// </summary>
    /// <param name="doc">Revit document</param>
    /// <param name="transactionName">Transaction name</param>
    /// <param name="action">Action to execute</param>
    public static void ExecuteInTransaction(Document doc, string transactionName, Action action)
    {
        using var transaction = new Transaction(doc, transactionName);
        transaction.Start();
        try
        {
            action();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            throw new Exception($"Error executing '{transactionName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Execute a series of operations within a transaction, returning a list of results
    /// </summary>
    /// <typeparam name="T">Input data type</typeparam>
    /// <typeparam name="R">Result data type</typeparam>
    /// <param name="doc">Revit document</param>
    /// <param name="transactionName">Transaction name</param>
    /// <param name="items">List of input objects</param>
    /// <param name="actionPerItem">Action to perform on each object</param>
    /// <returns>List of results</returns>
    public static List<R> ExecuteBatchInTransaction<T, R>(Document doc, string transactionName,
        IEnumerable<T> items, Func<T, R> actionPerItem)
    {
        var results = new List<R>();

        using var transaction = new Transaction(doc, transactionName);
        transaction.Start();
        try
        {
            foreach (var item in items)
            {
                var result = actionPerItem(item);
                results.Add(result);
            }

            transaction.Commit();
            return results;
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            throw new Exception($"Error executing batch '{transactionName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Execute a series of operations within a transaction (no return value)
    /// </summary>
    /// <typeparam name="T">Input data type</typeparam>
    /// <param name="doc">Revit document</param>
    /// <param name="transactionName">Transaction name</param>
    /// <param name="items">List of input objects</param>
    /// <param name="actionPerItem">Action to perform on each object</param>
    public static void ExecuteBatchInTransaction<T>(Document doc, string transactionName,
        IEnumerable<T> items, Action<T> actionPerItem)
    {
        using var transaction = new Transaction(doc, transactionName);
        transaction.Start();
        try
        {
            foreach (var item in items) actionPerItem(item);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            throw new Exception($"Error executing batch '{transactionName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Execute an operation within a transaction group (enables undo/redo as a group)
    /// </summary>
    /// <typeparam name="T">Result data type</typeparam>
    /// <param name="doc">Revit document</param>
    /// <param name="groupName">Transaction group name</param>
    /// <param name="action">Action to execute</param>
    /// <returns>Action result</returns>
    public static T ExecuteInTransactionGroup<T>(Document doc, string groupName, Func<T> action)
    {
        using var group = new TransactionGroup(doc, groupName);
        group.Start();
        try
        {
            var result = action();
            group.Assimilate();
            return result;
        }
        catch (Exception ex)
        {
            group.RollBack();
            throw new Exception($"Error executing transaction group '{groupName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Execute an operation within a transaction group (no return value)
    /// </summary>
    /// <param name="doc">Revit document</param>
    /// <param name="groupName">Transaction group name</param>
    /// <param name="action">Action to execute</param>
    public static void ExecuteInTransactionGroup(Document doc, string groupName, Action action)
    {
        using var group = new TransactionGroup(doc, groupName);
        group.Start();
        try
        {
            action();
            group.Assimilate();
        }
        catch (Exception ex)
        {
            group.RollBack();
            throw new Exception($"Error executing transaction group '{groupName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Execute material operation within a transaction
    /// </summary>
    /// <param name="doc">Revit document</param>
    /// <param name="element">Revit element</param>
    /// <param name="materialName">Material name</param>
    /// <returns>True if successful</returns>
    public static bool SetMaterialInTransaction(Document doc, Element element, string materialName)
    {
        return ExecuteInTransaction(doc, "Set Material", () =>
        {
            // Find material by name
            var material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
                return false;

            // Get material parameter
            var materialParam = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)
                                ?? element.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            if (materialParam != null && !materialParam.IsReadOnly)
            {
                materialParam.Set(material.Id);
                return true;
            }

            return false;
        });
    }

    /// <summary>
    ///     Wrap exception in a transaction to ensure rollback if error occurs
    /// </summary>
    /// <param name="doc">Revit document</param>
    /// <param name="transactionName">Transaction name</param>
    /// <param name="action">Action to execute</param>
    /// <param name="errorHandler">Custom error handler</param>
    /// <returns>True if successful</returns>
    public static bool SafeExecuteInTransaction(Document doc, string transactionName, Action action,
        Action<Exception> errorHandler = null)
    {
        using var transaction = new Transaction(doc, transactionName);
        transaction.Start();
        try
        {
            action();
            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            if (errorHandler != null) errorHandler(ex);
            return false;
        }
    }
}