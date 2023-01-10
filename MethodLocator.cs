using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;

namespace BabelDeobfuscator;

public sealed record class MethodLocator
{
    private IEnumerable<MethodDefinition> _methods;
    public MethodLocator(TypeDefinition declaringType)
    {
        _methods = declaringType.Methods;
    }

    public MethodLocator Exclude(params MethodDefinition[] methods)
    {
        return this with
        {
            _methods = _methods.Except(methods)
        };
    }

    public MethodLocator MatchesSignature(bool isStatic, TypeSignature returnType, TypeSignature[] parameterTypes)
    {
        return this with
        {
            _methods = _methods.Where(method =>
                    {
                        if (method.Signature == null)
                            return false;

                        if (method.Signature.HasThis != !isStatic)
                            return false;

                        if (method.Signature.ReturnType != returnType)
                            return false;

                        for (var i = 0; i < method.Signature.ParameterTypes.Count; i++)
                        {
                            if (method.Signature.ParameterTypes[i] != parameterTypes[i])
                                return false;
                        }
                        return true;
                    })
        };
    }

    public MethodLocator HasCilMethodBody()
    {
        return this with
        {
            _methods = _methods.Where(method => method.MethodBody is CilMethodBody)
        };
    }

    public MethodLocator HasInstruction(Func<CilInstruction, bool> predicate)
    {
        return this with
        {
            _methods = _methods.Where(method =>
            {
                if (method.CilMethodBody is not { Instructions: { } instructions })
                    return false;

                return instructions.Any(predicate);
            })
        };
    }

    public MethodDefinition First()
    {
        return _methods.FirstOrDefault() ?? throw new MissingMethodException();
    }
}