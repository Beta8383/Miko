using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using Miko.Attributes;

namespace Miko.HLSLBuffers;

static class ShaderBufferMapper<T>
{
    internal static ReadOnlyCollection<uint> Bindings { get; private set; } = null!;
    static Func<T, uint, HLSLBuffer> Mapper { get; set; } = null!;

    static ShaderBufferMapper()
    {
        CreateMapper();
    }

    /// <summary>
    /// Get HLSLBuffer corresponding to binding
    /// </summary>
    /// <param name="bufferStructure">The source buffer structure</param>
    /// <param name="binding">The binding index</param>
    /// <returns>The HLSLBuffer corresponding to binding</returns>
    /// <exception cref="Exception">The buffer structure is null.</exception>
    public static HLSLBuffer GetBuffer(T bufferStructure, uint binding)
    {
        return Mapper(bufferStructure, binding) ?? throw new Exception("HLSLBuffer is not bound to a device memory");
    }

    public static HLSLBuffer[] GetBuffers(T bufferStructure)
    {
        return Bindings.Select(binding => GetBuffer(bufferStructure, binding)).ToArray();
    }

    internal static void CreateMapper()
    {
        try
        {
            //get HLSLBuffer fields
            FieldInfo[] HLSLBufferFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                  .Where(field => typeof(HLSLBuffer).IsAssignableFrom(field.FieldType)).ToArray();

            //get binding attributes
            BufferAttribute?[] bufferAttributes = HLSLBufferFields.Select(field => field.GetCustomAttribute<BufferAttribute>()).ToArray();
            if (!ValidateBinding(bufferAttributes))
                throw new Exception("Buffer Attributes are not valid");
            //get binding attributes value
            uint[] bindingValue = bufferAttributes.Select(attribute => attribute!.Binding).ToArray();
            BufferType[] bufferTypes = bufferAttributes.Select(attribute => attribute!.Type).ToArray();
            //sort binding attributes value and save in the Bindings property
            Bindings = new ReadOnlyCollection<uint>(bindingValue.OrderBy(value => value).ToArray());

            //input parameters
            ParameterExpression bufferStructureParameter = Expression.Parameter(typeof(T));
            ParameterExpression bindingParameter = Expression.Parameter(typeof(uint));

            //get HLSLBuffer fields corresponding to binding attributes
            UnaryExpression[] HLSLBufferExpressions = HLSLBufferFields.Select(field => Expression.Convert(Expression.Field(bufferStructureParameter, field.Name), typeof(HLSLBuffer))).ToArray();
            //binding attributes value to HLSLBuffer fields in switch cases
            SwitchCase[] switchCases = HLSLBufferExpressions.Select((expression, index) => Expression.SwitchCase(HLSLBufferExpressions[index], Expression.Constant(bindingValue[index]))).ToArray();
            //exception expression when binding attributes value is not found
            ConstantExpression nullExpression = Expression.Constant(null, typeof(HLSLBuffer));
            //generate switch expression
            SwitchExpression HLSLBufferSwitchExpression = Expression.Switch(bindingParameter, nullExpression, switchCases);
            //generate mapping expression using switch expression
            Expression<Func<T, uint, HLSLBuffer>> mappingExpression = Expression.Lambda<Func<T, uint, HLSLBuffer>>(HLSLBufferSwitchExpression, bufferStructureParameter, bindingParameter);
            //compile mapping expression
            Mapper = mappingExpression.Compile();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static bool ValidateBinding(BufferAttribute?[] bindingAttributes)
    {
        //no null binding attributes and no duplicate binding attributes
        return bindingAttributes.All(bindingAttribute => bindingAttribute is not null) &&
               bindingAttributes.Distinct().Count() == bindingAttributes.Length;
    }
}