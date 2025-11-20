using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binah.Pipeline.Data.Migrations
{
    /// <summary>
    /// Migration to add node-graph support to pipelines table
    /// Adds 'nodes' and 'edges' JSONB columns for ReactFlow visual pipeline builder
    /// </summary>
    public partial class AddNodeGraphSupportToPipelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nodes column for storing pipeline node graph
            migrationBuilder.AddColumn<string>(
                name: "nodes",
                table: "pipelines",
                type: "jsonb",
                nullable: true);

            // Add edges column for storing connections between nodes
            migrationBuilder.AddColumn<string>(
                name: "edges",
                table: "pipelines",
                type: "jsonb",
                nullable: true);

            // Add comment explaining the purpose of these columns
            migrationBuilder.Sql(@"
                COMMENT ON COLUMN pipelines.nodes IS 'Visual pipeline nodes for ReactFlow (PipelineNode[])';
                COMMENT ON COLUMN pipelines.edges IS 'Connections between pipeline nodes (PipelineEdge[])';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "edges",
                table: "pipelines");

            migrationBuilder.DropColumn(
                name: "nodes",
                table: "pipelines");
        }
    }
}
