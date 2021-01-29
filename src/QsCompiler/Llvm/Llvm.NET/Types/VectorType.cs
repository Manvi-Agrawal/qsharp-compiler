﻿// -----------------------------------------------------------------------
// <copyright file="VectorType.cs" company="Ubiquity.NET Contributors">
// Copyright (c) Ubiquity.NET Contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;

using LLVMSharp.Interop;
using Ubiquity.NET.Llvm.Properties;

// Interface+internal type matches file name
#pragma warning disable SA1649

namespace Ubiquity.NET.Llvm.Types
{
    /// <summary>Interface for an LLVM vector type</summary>
    public interface IVectorType
        : ISequenceType
    {
        /// <summary>Gets the number of elements in the vector</summary>
        uint Size { get; }
    }

    internal class VectorType
        : SequenceType
        , IVectorType
    {
        public uint Size => TypeRefHandle.VectorSize;

        internal VectorType( LLVMTypeRef typeRef )
            : base( typeRef )
        {
            if( typeRef.Kind != LLVMTypeKind.LLVMVectorTypeKind )
            {
                throw new ArgumentException( Resources.Vector_type_reference_expected, nameof( typeRef ) );
            }
        }
    }
}